using System.Text.Json.Nodes;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;
using Zsif.Energy.Worker.Mqtt;

namespace Zsif.Energy.Worker.Test;

public class GridMeterSensorsTest
{
    private static readonly MqttOptions Options = new() { Host = "broker.test" };
    private static readonly DateOnly Day = new(2026, 11, 8);

    [Fact]
    public void TestAllSensorsHaveUniqueObjectIds()
    {
        var objectIds = GridMeterSensors.All.Select(s => s.ObjectId).ToList();

        Assert.Equal(9, objectIds.Count);
        Assert.Equal(objectIds.Count, objectIds.Distinct().Count());
    }

    [Theory]
    [InlineData("grid_import_energy_today")]
    [InlineData("grid_import_energy_yesterday")]
    [InlineData("grid_import_power")]
    [InlineData("grid_import_meter_reading")]
    [InlineData("grid_export_energy_today")]
    [InlineData("grid_export_energy_yesterday")]
    [InlineData("grid_export_power")]
    [InlineData("grid_export_meter_reading")]
    [InlineData("grid_meter_last_update")]
    public void TestSensorExists(string objectId)
    {
        Assert.Contains(GridMeterSensors.All, s => s.ObjectId == objectId);
    }

    [Fact]
    public void TestDiscoveryTopicUsesOwnNodeId()
    {
        var sensor = Sensor("grid_import_energy_today");

        Assert.Equal("homeassistant/sensor/zsif_gridmeter/grid_import_energy_today/config", GridMeterSensors.DiscoveryTopic(Options, sensor));
    }

    [Fact]
    public void TestDiscoveryPayloadForEnergyToday()
    {
        var payload = Parse(GridMeterSensors.BuildDiscoveryPayload(Options, Sensor("grid_import_energy_today")));

        Assert.Equal("Grid import energy today", (string?)payload["name"]);
        Assert.Equal("zsif_gridmeter_grid_import_energy_today", (string?)payload["unique_id"]);
        Assert.Equal("sensor.grid_import_energy_today", (string?)payload["default_entity_id"]);
        Assert.Equal("zsif/gridmeter/import/today", (string?)payload["state_topic"]);
        Assert.Equal("zsif/gridmeter/import/today", (string?)payload["json_attributes_topic"]);
        Assert.Equal("{{ value_json.energy_kwh }}", (string?)payload["value_template"]);
        Assert.Equal("energy", (string?)payload["device_class"]);
        Assert.Equal("kWh", (string?)payload["unit_of_measurement"]);
        Assert.Equal("total_increasing", (string?)payload["state_class"]);
        Assert.Equal("zsif/gridmeter/status", (string?)payload["availability_topic"]);
        Assert.Equal("zsif_gridmeter", (string?)payload["device"]!["identifiers"]![0]);
    }

    [Fact]
    public void TestDiscoveryPayloadForYesterdayHasNoStateClass()
    {
        var payload = Parse(GridMeterSensors.BuildDiscoveryPayload(Options, Sensor("grid_export_energy_yesterday")));

        Assert.False(payload.ContainsKey("state_class"));
        Assert.Equal("zsif/gridmeter/export/yesterday", (string?)payload["state_topic"]);
    }

    [Fact]
    public void TestDiscoveryPayloadForPower()
    {
        var payload = Parse(GridMeterSensors.BuildDiscoveryPayload(Options, Sensor("grid_export_power")));

        Assert.Equal("power", (string?)payload["device_class"]);
        Assert.Equal("W", (string?)payload["unit_of_measurement"]);
        Assert.Equal("measurement", (string?)payload["state_class"]);
        Assert.Equal("zsif/gridmeter/export/latest", (string?)payload["state_topic"]);
        Assert.Equal("{{ value_json.power_w }}", (string?)payload["value_template"]);
    }

    [Fact]
    public void TestDiscoveryPayloadForLastUpdateUsesPlainValue()
    {
        var payload = Parse(GridMeterSensors.BuildDiscoveryPayload(Options, Sensor("grid_meter_last_update")));

        Assert.Equal("timestamp", (string?)payload["device_class"]);
        Assert.Equal("zsif/gridmeter/last_update", (string?)payload["state_topic"]);
        Assert.False(payload.ContainsKey("value_template"));
        Assert.False(payload.ContainsKey("unit_of_measurement"));
    }

    [Fact]
    public void TestDiscoveryPayloadUsesConfiguredPrefixes()
    {
        var options = new MqttOptions { Host = "broker.test", TopicPrefix = "custom/meter", DiscoveryPrefix = "ha", NodeId = "custom_node" };
        var sensor = Sensor("grid_import_power");
        var payload = Parse(GridMeterSensors.BuildDiscoveryPayload(options, sensor));

        Assert.Equal("ha/sensor/custom_node/grid_import_power/config", GridMeterSensors.DiscoveryTopic(options, sensor));
        Assert.Equal("custom/meter/import/latest", (string?)payload["state_topic"]);
        Assert.Equal("custom_node_grid_import_power", (string?)payload["unique_id"]);
    }

    [Fact]
    public void TestDayPayload()
    {
        var summary = new MeterDaySummary(MeterDirection.Export, Day, 0.819m, 96, 96, 0m, new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(1)), 5609.072m);

        var payload = Parse(GridMeterSensors.BuildDayPayload(summary));

        Assert.Equal("2026-11-08", (string?)payload["day"]);
        Assert.Equal(0.819m, (decimal?)payload["energy_kwh"]);
        Assert.Equal(96, (int?)payload["intervals"]);
        Assert.Equal(96, (int?)payload["expected_intervals"]);
        Assert.Equal("2026-11-09T00:00:00+01:00", (string?)payload["last_interval_end"]);
    }

    [Fact]
    public void TestDayPayloadWithoutDataHasNullEnergy()
    {
        var summary = new MeterDaySummary(MeterDirection.Import, Day, null, 0, 96, null, null, null);

        var json = GridMeterSensors.BuildDayPayload(summary);

        Assert.Contains("\"energy_kwh\":null", json);
        Assert.Contains("\"last_interval_end\":null", json);
    }

    [Fact]
    public void TestLatestPayload()
    {
        var summary = new MeterDaySummary(MeterDirection.Import, Day, 53.404m, 53, 96, 13236m, new DateTimeOffset(2026, 11, 9, 13, 15, 0, TimeSpan.FromHours(1)), 39113.981m);

        var payload = Parse(GridMeterSensors.BuildLatestPayload(summary));

        Assert.Equal(13236m, (decimal?)payload["power_w"]);
        Assert.Equal(39113.981m, (decimal?)payload["reading_kwh"]);
        Assert.Equal("2026-11-09T13:15:00+01:00", (string?)payload["last_interval_end"]);
    }

    [Fact]
    public void TestSelectLatestPrefersToday()
    {
        var today = Summary(Day.AddDays(1), 5);
        var yesterday = Summary(Day, 96);

        Assert.Same(today, GridMeterSensors.SelectLatest(today, yesterday));
    }

    [Fact]
    public void TestSelectLatestFallsBackToYesterdayAfterMidnight()
    {
        var today = Summary(Day.AddDays(1), 0);
        var yesterday = Summary(Day, 96);

        Assert.Same(yesterday, GridMeterSensors.SelectLatest(today, yesterday));
    }

    [Fact]
    public void TestSelectLatestReturnsNullWithoutAnyData()
    {
        Assert.Null(GridMeterSensors.SelectLatest(Summary(Day.AddDays(1), 0), Summary(Day, 0)));
    }

    private static SensorDefinition Sensor(string objectId)
    {
        return GridMeterSensors.All.Single(s => s.ObjectId == objectId);
    }

    private static MeterDaySummary Summary(DateOnly day, int intervals)
    {
        return new MeterDaySummary(MeterDirection.Import, day, intervals > 0 ? 1m : null, intervals, 96, null, null, null);
    }

    private static JsonObject Parse(string json)
    {
        return JsonNode.Parse(json)!.AsObject();
    }
}
