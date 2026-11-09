namespace Zsif.Energy.Worker.Mqtt;

public sealed record SensorDefinition(
    string ObjectId,
    string Name,
    string StateTopicSuffix,
    string? ValueKey,
    string? DeviceClass,
    string? Unit,
    string? StateClass);
