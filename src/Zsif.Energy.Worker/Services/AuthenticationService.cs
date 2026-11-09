using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;

namespace Zsif.Energy.Worker.Services;

public sealed class AuthenticationService(
    IHttpClientFactory httpClientFactory,
    IOptions<SmartmeterOptions> options,
    ILogger<AuthenticationService> logger)
{
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _token;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = Volatile.Read(ref _token);
        if (token is not null)
        {
            return token;
        }

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            _token ??= await LoginAsync(cancellationToken);
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    //Only clears the token if no other caller already replaced it
    public void InvalidateToken(string token)
    {
        Interlocked.CompareExchange(ref _token, null, token);
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(SmartmeterClient.HttpClientName);
        var request = new LoginRequest(settings.Username, settings.Password, false);

        using var response = await client.PostAsJsonAsync("api/account/login", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmartmeterException($"Login failed with HTTP {(int)response.StatusCode}");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        var token = login?.Data?.LinkedAccounts.FirstOrDefault()?.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new SmartmeterException("Login response contains no token");
        }

        logger.LogInformation("Logged in to Netz Burgenland smart meter portal");
        return token;
    }
}
