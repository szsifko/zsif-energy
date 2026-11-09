using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;
using Zsif.Energy.Worker.Services;

namespace Zsif.Energy.Worker.Test;

public class SmartmeterClientTest
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private static readonly DayBounds Day = DayBounds.Create(new DateOnly(2026, 11, 8), Vienna);

    private static readonly SmartmeterOptions Settings = new()
    {
        Username = "user@example.test",
        Password = "secret",
        ExportMeteringPoint = "AT00EXPORT",
        ImportMeteringPoint = "AT00IMPORT"
    };

    [Fact]
    public void TestBuildValuesUriDoubleEncodesTimestamps()
    {
        var uri = SmartmeterClient.BuildValuesUri("AT00EXPORT", "1-0:2.8.0*H15", "Einspeisung", Day.From, Day.To);

        Assert.Equal(
            "api/MeterReadingSequence/values/normal?meteringPointIdentifier=AT00EXPORT&valueIdentifier=1-0:2.8.0*H15"
            + "&from=2026-11-08T00%253A00%253A00%252B01%253A00&to=2026-11-09T00%253A00%253A00%252B01%253A00"
            + "&name=Einspeisung&timeParameter=original",
            uri);
    }

    [Fact]
    public async Task TestGetDayReadingsParsesSampleResponse()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : Json(File.ReadAllText(Path.Combine("Resources", "meter-response-sample.json"))));
        var client = CreateClient(handler);

        var readings = await client.GetDayReadingsAsync(MeterDirection.Import, Day, CancellationToken.None);

        Assert.Equal(3, readings.Count);
        Assert.Equal(0.09200000000419095m, readings[0].Value);
        Assert.Equal(39044.181000000004m, readings[0].Reading);
        Assert.Equal(Day.From, readings[0].StartTimestamp);
        Assert.Null(readings[2].Value);
    }

    [Fact]
    public async Task TestGetDayReadingsUsesMeteringPointPerDirection()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : Json("""[{"name":"Einspeisung","data":[]}]"""));
        var client = CreateClient(handler);

        await client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None);
        await client.GetDayReadingsAsync(MeterDirection.Import, Day, CancellationToken.None);

        var valueRequests = handler.Requests.Where(r => !r.Uri.EndsWith("login")).ToList();
        Assert.Contains("meteringPointIdentifier=AT00EXPORT", valueRequests[0].Uri);
        Assert.Contains("valueIdentifier=1-0:2.8.0*H15", valueRequests[0].Uri);
        Assert.Contains("meteringPointIdentifier=AT00IMPORT", valueRequests[1].Uri);
        Assert.Contains("valueIdentifier=1-0:1.8.0*H15", valueRequests[1].Uri);
    }

    [Fact]
    public async Task TestGetDayReadingsLogsInOnlyOnce()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : Json("""[{"name":"Einspeisung","data":[]}]"""));
        var client = CreateClient(handler);

        await client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None);
        await client.GetDayReadingsAsync(MeterDirection.Import, Day, CancellationToken.None);

        Assert.Single(handler.Requests, r => r.Uri.EndsWith("login"));
        Assert.All(handler.Requests.Where(r => !r.Uri.EndsWith("login")), r => Assert.Equal("token-1", r.Authorization));
    }

    [Fact]
    public async Task TestGetDayReadingsReloginsOnUnauthorized()
    {
        var logins = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login"))
            {
                logins++;
                return Json(LoginJson($"token-{logins}"));
            }

            return request.Headers.GetValues("Authorization").Single() == "token-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json("""[{"name":"Einspeisung","data":[]}]""");
        });
        var client = CreateClient(handler);

        var readings = await client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None);

        Assert.Empty(readings);
        Assert.Equal(2, logins);
        Assert.Equal("token-2", handler.Requests[^1].Authorization);
    }

    [Fact]
    public async Task TestGetDayReadingsThrowsOnServerError()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SmartmeterException>(
            () => client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None));

        Assert.Contains("HTTP 500", exception.Message);
    }

    [Fact]
    public async Task TestLoginThrowsWithoutToken()
    {
        var handler = new StubHandler(_ => Json("""{"data":{"linkedAccounts":[]}}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SmartmeterException>(
            () => client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None));

        Assert.Equal("Login response contains no token", exception.Message);
    }

    [Fact]
    public async Task TestLoginErrorDoesNotContainPassword()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SmartmeterException>(
            () => client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None));

        Assert.Contains("HTTP 400", exception.Message);
        Assert.DoesNotContain(Settings.Password, exception.Message);
    }

    [Fact]
    public async Task TestGetDayReadingsThrowsWithResponseSnippetOnUnexpectedFormat()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : Json("""{"error":"unexpected"}"""));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SmartmeterException>(
            () => client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None));

        Assert.Contains("""{"error":"unexpected"}""", exception.Message);
    }

    [Fact]
    public async Task TestGetDayReadingsCombinesAllSeries()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(LoginJson("token-1"))
            : Json("""
                [
                  {"name":"Einspeisung","data":[{"startTimestamp":"2026-11-08T00:00:00+01:00","endTimestamp":"2026-11-08T00:15:00+01:00","value":0.1}]},
                  {"name":"Einspeisung","data":[{"startTimestamp":"2026-11-08T00:15:00+01:00","endTimestamp":"2026-11-08T00:30:00+01:00","value":0.2}]}
                ]
                """));
        var client = CreateClient(handler);

        var readings = await client.GetDayReadingsAsync(MeterDirection.Export, Day, CancellationToken.None);

        Assert.Equal(2, readings.Count);
    }

    private static SmartmeterClient CreateClient(StubHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(Settings);
        var authentication = new AuthenticationService(factory, options, NullLogger<AuthenticationService>.Instance);
        return new SmartmeterClient(factory, authentication, options);
    }

    private static string LoginJson(string token)
    {
        return $$$"""{"data":{"linkedAccounts":[{"token":"{{{token}}}"}]}}""";
    }

    private static HttpResponseMessage Json(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed record RecordedRequest(string Uri, string? Authorization);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
            Requests.Add(new RecordedRequest(request.RequestUri!.OriginalString, authorization));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://portal.example.test/enVIEW/enVIEW.Portal.Api/")
            };
        }
    }
}
