using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;
using Zsif.Energy.Worker.Services;

namespace Zsif.Energy.Worker;

public sealed class SmartmeterClient(
    IHttpClientFactory httpClientFactory,
    AuthenticationService authenticationService,
    IOptions<SmartmeterOptions> options)
{
    public const string HttpClientName = "Smartmeter";

    private const string ExportValueIdentifier = "1-0:2.8.0*H15";
    private const string ImportValueIdentifier = "1-0:1.8.0*H15";

    public async Task<IReadOnlyList<MeterReading>> GetDayReadingsAsync(
        MeterDirection direction,
        DayBounds bounds,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var uri = direction == MeterDirection.Export
            ? BuildValuesUri(settings.ExportMeteringPoint, ExportValueIdentifier, "Einspeisung", bounds.From, bounds.To)
            : BuildValuesUri(settings.ImportMeteringPoint, ImportValueIdentifier, "Bezug", bounds.From, bounds.To);

        using var response = await SendAuthorizedAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmartmeterException($"Fetching {direction} values for {bounds.Day:yyyy-MM-dd} failed with HTTP {(int)response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var series = JsonSerializer.Deserialize<List<MeterSeries>>(content, JsonSerializerOptions.Web);
            return series?.SelectMany(s => s.Data).ToList() ?? [];
        }
        catch (JsonException ex)
        {
            throw new SmartmeterException($"Unexpected {direction} response for {bounds.Day:yyyy-MM-dd} ({ex.Message}): {Truncate(content, 300)}");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    //The portal requires double URL-encoded timestamps, single encoding returns HTTP 500
    public static string BuildValuesUri(
        string meteringPoint,
        string valueIdentifier,
        string name,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        return "api/MeterReadingSequence/values/normal"
            + $"?meteringPointIdentifier={Uri.EscapeDataString(meteringPoint)}"
            + $"&valueIdentifier={valueIdentifier}"
            + $"&from={DoubleEncode(from)}"
            + $"&to={DoubleEncode(to)}"
            + $"&name={Uri.EscapeDataString(name)}"
            + "&timeParameter=original";
    }

    private static string DoubleEncode(DateTimeOffset timestamp)
    {
        var formatted = timestamp.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        return Uri.EscapeDataString(Uri.EscapeDataString(formatted));
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(string uri, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var token = await authenticationService.GetTokenAsync(cancellationToken);
        var response = await client.SendAsync(CreateRequest(uri, token), cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            //Token expired: log in again and retry once
            response.Dispose();
            authenticationService.InvalidateToken(token);
            token = await authenticationService.GetTokenAsync(cancellationToken);
            response = await client.SendAsync(CreateRequest(uri, token), cancellationToken);
        }

        return response;
    }

    private static HttpRequestMessage CreateRequest(string uri, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", token);
        return request;
    }
}
