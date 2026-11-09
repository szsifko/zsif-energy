using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Mqtt;

namespace Zsif.Energy.Worker;

public sealed class MqttPublisher : BackgroundService
{
    private const string Online = "online";
    private const string Offline = "offline";

    private readonly MqttOptions _options;
    private readonly ILogger<MqttPublisher> _logger;
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public MqttPublisher(IOptions<MqttOptions> options, ILogger<MqttPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += args =>
        {
            if (args.ClientWasConnected)
            {
                _logger.LogWarning($"Disconnected from MQTT broker: {args.Reason}");
            }

            return Task.CompletedTask;
        };
    }

    public bool IsConnected => _client.IsConnected;

    public async Task PublishAsync(string topicSuffix, string payload, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
        await PublishRetainedAsync(GridMeterSensors.StateTopic(_options, topicSuffix), payload, cancellationToken);
    }

    //Keeps the connection alive between polls so availability in Home Assistant stays accurate
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning($"MQTT connection to {_options.Host}:{_options.Port} failed: {ex.Message}");
            }

            await Task.Delay(_options.ReconnectInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (!_client.IsConnected)
        {
            return;
        }

        try
        {
            //A clean disconnect does not trigger the last will, so report offline explicitly
            await PublishRetainedAsync(GridMeterSensors.StateTopic(_options, GridMeterSensors.StatusSuffix), Offline, cancellationToken);
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"MQTT disconnect failed: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        _client.Dispose();
        _connectLock.Dispose();
        base.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            await _client.ConnectAsync(BuildClientOptions(), cancellationToken);
            _logger.LogInformation($"Connected to MQTT broker {_options.Host}:{_options.Port}");

            await PublishRetainedAsync(GridMeterSensors.StateTopic(_options, GridMeterSensors.StatusSuffix), Online, cancellationToken);
            foreach (var sensor in GridMeterSensors.All)
            {
                await PublishRetainedAsync(
                    GridMeterSensors.DiscoveryTopic(_options, sensor),
                    GridMeterSensors.BuildDiscoveryPayload(_options, sensor),
                    cancellationToken);
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(GridMeterSensors.StateTopic(_options, GridMeterSensors.StatusSuffix))
            .WithWillPayload(Offline)
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            builder.WithCredentials(_options.Username, _options.Password);
        }

        return builder.Build();
    }

    private async Task PublishRetainedAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(true)
            .Build();

        var result = await _client.PublishAsync(message, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new SmartmeterException($"MQTT publish to {topic} failed: {result.ReasonCode}");
        }
    }
}
