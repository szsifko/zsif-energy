using System.ComponentModel.DataAnnotations;

namespace Zsif.Energy.Worker.Configuration;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }

    [Required]
    public string ClientId { get; set; } = "zsif-gridmeter";

    [Required]
    public string TopicPrefix { get; set; } = "zsif/gridmeter";

    [Required]
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    //Own node id keeps discovery topics apart from Zigbee2MQTT
    [Required]
    public string NodeId { get; set; } = "zsif_gridmeter";

    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(30);
}
