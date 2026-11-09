using System.Text.Json.Serialization;

namespace Zsif.Energy.Worker.Models;

public sealed record LoginRequest(
    [property: JsonPropertyName("usernameOrEMail")] string UsernameOrEMail,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("rememberMe")] bool RememberMe);

public sealed class LoginResponse
{
    public LoginData? Data { get; set; }
}

public sealed class LoginData
{
    public List<LinkedAccount> LinkedAccounts { get; set; } = [];
}

public sealed class LinkedAccount
{
    public string? Token { get; set; }
}
