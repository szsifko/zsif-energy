using System.Globalization;
using System.Text.Json.Nodes;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;

namespace Zsif.Energy.Worker.Mqtt;

public static class GridMeterSensors
{
    public const string LastUpdateSuffix = "last_update";
    public const string StatusSuffix = "status";

    private const string DeviceName = "Netz Burgenland Smart Meter";

    public static IReadOnlyList<SensorDefinition> All { get; } =
    [
        .. DirectionSensors(MeterDirection.Import),
        .. DirectionSensors(MeterDirection.Export),
        new("grid_meter_last_update", "Grid meter last update", LastUpdateSuffix, null, "timestamp", null, null)
    ];

    public static string DirectionKey(MeterDirection direction)
    {
        return direction == MeterDirection.Import ? "import" : "export";
    }

    public static string DiscoveryTopic(MqttOptions options, SensorDefinition sensor)
    {
        return $"{options.DiscoveryPrefix}/sensor/{options.NodeId}/{sensor.ObjectId}/config";
    }

    public static string StateTopic(MqttOptions options, string suffix)
    {
        return $"{options.TopicPrefix}/{suffix}";
    }

    public static string BuildDiscoveryPayload(MqttOptions options, SensorDefinition sensor)
    {
        var stateTopic = StateTopic(options, sensor.StateTopicSuffix);
        var payload = new JsonObject
        {
            ["name"] = sensor.Name,
            ["unique_id"] = $"{options.NodeId}_{sensor.ObjectId}",
            ["default_entity_id"] = $"sensor.{sensor.ObjectId}",
            ["state_topic"] = stateTopic,
            ["availability_topic"] = StateTopic(options, StatusSuffix),
            ["payload_available"] = "online",
            ["payload_not_available"] = "offline",
            ["device"] = new JsonObject
            {
                ["identifiers"] = new JsonArray(options.NodeId),
                ["name"] = DeviceName,
                ["manufacturer"] = "Netz Burgenland",
                ["model"] = "Smart meter portal"
            },
            ["origin"] = new JsonObject
            {
                ["name"] = "zsif-energy"
            }
        };

        if (sensor.ValueKey is not null)
        {
            payload["value_template"] = $"{{{{ value_json.{sensor.ValueKey} }}}}";
            payload["json_attributes_topic"] = stateTopic;
        }

        AddIfNotNull(payload, "device_class", sensor.DeviceClass);
        AddIfNotNull(payload, "unit_of_measurement", sensor.Unit);
        AddIfNotNull(payload, "state_class", sensor.StateClass);

        return payload.ToJsonString();
    }

    public static string BuildDayPayload(MeterDaySummary summary)
    {
        return new JsonObject
        {
            ["day"] = summary.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["energy_kwh"] = summary.EnergyKwh,
            ["intervals"] = summary.IntervalCount,
            ["expected_intervals"] = summary.ExpectedIntervalCount,
            ["last_interval_end"] = FormatTimestamp(summary.LastIntervalEnd)
        }.ToJsonString();
    }

    public static string BuildLatestPayload(MeterDaySummary summary)
    {
        return new JsonObject
        {
            ["power_w"] = summary.LastIntervalPowerW,
            ["reading_kwh"] = summary.LastReadingKwh,
            ["last_interval_end"] = FormatTimestamp(summary.LastIntervalEnd)
        }.ToJsonString();
    }

    //Today's data if any interval is delivered yet, otherwise the end of yesterday
    public static MeterDaySummary? SelectLatest(MeterDaySummary today, MeterDaySummary yesterday)
    {
        if (today.IntervalCount > 0)
        {
            return today;
        }

        return yesterday.IntervalCount > 0 ? yesterday : null;
    }

    public static string? FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<SensorDefinition> DirectionSensors(MeterDirection direction)
    {
        var key = DirectionKey(direction);
        return
        [
            new($"grid_{key}_energy_today", $"Grid {key} energy today", $"{key}/today", "energy_kwh", "energy", "kWh", "total_increasing"),
            new($"grid_{key}_energy_yesterday", $"Grid {key} energy yesterday", $"{key}/yesterday", "energy_kwh", "energy", "kWh", null),
            new($"grid_{key}_power", $"Grid {key} power", $"{key}/latest", "power_w", "power", "W", "measurement"),
            new($"grid_{key}_meter_reading", $"Grid {key} meter reading", $"{key}/latest", "reading_kwh", "energy", "kWh", "total_increasing")
        ];
    }

    private static void AddIfNotNull(JsonObject payload, string key, string? value)
    {
        if (value is not null)
        {
            payload[key] = value;
        }
    }
}
