using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Server;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Mqtt;

namespace Zsif.Energy.Worker.Test;

//Runs against an embedded MQTT broker
public class MqttPublisherTest : IAsyncLifetime
{
    private readonly int _port = GetFreePort();
    private MqttServer _server = null!;
    private string? _connectedUsername;
    private string? _connectedPassword;

    public async Task InitializeAsync()
    {
        var factory = new MqttServerFactory();
        var options = factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(_port)
            .Build();
        _server = factory.CreateMqttServer(options);
        _server.ValidatingConnectionAsync += args =>
        {
            _connectedUsername = args.UserName;
            _connectedPassword = args.Password;
            return Task.CompletedTask;
        };
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task TestPublishConnectsAndSendsDiscoveryAndStatus()
    {
        using var publisher = CreatePublisher();

        await publisher.PublishAsync("import/today", """{"energy_kwh":1.5}""", CancellationToken.None);

        var retained = await GetRetainedAsync();
        Assert.Equal("online", retained["zsif/gridmeter/status"]);
        Assert.Equal("""{"energy_kwh":1.5}""", retained["zsif/gridmeter/import/today"]);
        foreach (var sensor in GridMeterSensors.All)
        {
            Assert.True(retained.ContainsKey($"homeassistant/sensor/zsif_gridmeter/{sensor.ObjectId}/config"), sensor.ObjectId);
        }
    }

    [Fact]
    public async Task TestPublishSendsCredentials()
    {
        using var publisher = CreatePublisher();

        await publisher.PublishAsync("import/today", "{}", CancellationToken.None);

        Assert.Equal("gridmeter", _connectedUsername);
        Assert.Equal("mqtt-secret", _connectedPassword);
    }

    [Fact]
    public async Task TestPublishOverwritesRetainedValue()
    {
        using var publisher = CreatePublisher();

        await publisher.PublishAsync("export/latest", """{"power_w":100}""", CancellationToken.None);
        await publisher.PublishAsync("export/latest", """{"power_w":200}""", CancellationToken.None);

        var retained = await GetRetainedAsync();
        Assert.Equal("""{"power_w":200}""", retained["zsif/gridmeter/export/latest"]);
    }

    [Fact]
    public async Task TestStopPublishesOffline()
    {
        using var publisher = CreatePublisher();
        await publisher.PublishAsync("import/today", "{}", CancellationToken.None);

        await publisher.StopAsync(CancellationToken.None);

        var retained = await GetRetainedAsync();
        Assert.Equal("offline", retained["zsif/gridmeter/status"]);
        Assert.False(publisher.IsConnected);
    }

    [Fact]
    public async Task TestPublishReconnectsAfterDisconnect()
    {
        using var publisher = CreatePublisher();
        await publisher.PublishAsync("import/today", """{"energy_kwh":1}""", CancellationToken.None);

        await _server.StopAsync();
        await WaitUntilAsync(() => !publisher.IsConnected);
        await _server.StartAsync();

        await publisher.PublishAsync("import/today", """{"energy_kwh":2}""", CancellationToken.None);

        Assert.True(publisher.IsConnected);
        var retained = await GetRetainedAsync();
        Assert.Equal("""{"energy_kwh":2}""", retained["zsif/gridmeter/import/today"]);
    }

    [Fact]
    public async Task TestPublishThrowsWhenBrokerUnreachable()
    {
        await _server.StopAsync();
        using var publisher = CreatePublisher();

        await Assert.ThrowsAnyAsync<Exception>(() => publisher.PublishAsync("import/today", "{}", CancellationToken.None));
        Assert.False(publisher.IsConnected);
    }

    private MqttPublisher CreatePublisher()
    {
        var options = Options.Create(new MqttOptions
        {
            Host = "127.0.0.1",
            Port = _port,
            Username = "gridmeter",
            Password = "mqtt-secret",
            ClientId = $"test-{Guid.NewGuid():N}"
        });
        return new MqttPublisher(options, NullLogger<MqttPublisher>.Instance);
    }

    private async Task<Dictionary<string, string>> GetRetainedAsync()
    {
        var messages = await _server.GetRetainedMessagesAsync();
        return messages.ToDictionary(m => m.Topic, m => m.ConvertPayloadToString());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.Now.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.Now > timeout)
            {
                throw new TimeoutException("Condition not met");
            }

            await Task.Delay(50);
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
