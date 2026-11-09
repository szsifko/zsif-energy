using System.ComponentModel.DataAnnotations;

namespace Zsif.Energy.Worker.Configuration;

public sealed class SmartmeterOptions
{
    public const string SectionName = "Smartmeter";

    [Required]
    public string BaseUrl { get; set; } = "https://smartmeter.netzburgenland.at/enVIEW/enVIEW.Portal.Api/";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string ExportMeteringPoint { get; set; } = string.Empty;

    [Required]
    public string ImportMeteringPoint { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    [Required]
    public string TimeZone { get; set; } = "Europe/Vienna";
}
