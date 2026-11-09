using System.Globalization;
using Microsoft.Extensions.Options;
using Zsif.Energy.Worker.Configuration;
using Zsif.Energy.Worker.Models;
using Zsif.Energy.Worker.Mqtt;
using Zsif.Energy.Worker.Services;

namespace Zsif.Energy.Worker;

public sealed class SmartmeterWorker(
    SmartmeterClient smartmeterClient,
    MeterCalculationService calculationService,
    MqttPublisher mqttPublisher,
    TimeProvider timeProvider,
    IOptions<SmartmeterOptions> options,
    ILogger<SmartmeterWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
        logger.LogInformation($"Polling smart meter portal every {settings.PollInterval}");

        using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);
        do
        {
            await PollAsync(timeZone, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var today = DayBounds.Today(timeProvider, timeZone);
        var todayBounds = DayBounds.Create(today, timeZone);
        var yesterdayBounds = DayBounds.Create(today.AddDays(-1), timeZone);
        var allFetched = true;

        foreach (var direction in Enum.GetValues<MeterDirection>())
        {
            var yesterdaySummary = await TryFetchAsync(direction, yesterdayBounds, timeZone, cancellationToken);
            var todaySummary = await TryFetchAsync(direction, todayBounds, timeZone, cancellationToken);
            if (yesterdaySummary is null
                || todaySummary is null)
            {
                allFetched = false;
            }

            await TryPublishAsync(() => PublishDirectionAsync(direction, todaySummary, yesterdaySummary, cancellationToken), cancellationToken);
        }

        if (allFetched)
        {
            var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
            await TryPublishAsync(
                () => mqttPublisher.PublishAsync(GridMeterSensors.LastUpdateSuffix, GridMeterSensors.FormatTimestamp(now)!, cancellationToken),
                cancellationToken);
        }
    }

    //Failed fetches publish nothing, so Home Assistant keeps the last valid values
    private async Task PublishDirectionAsync(
        MeterDirection direction,
        MeterDaySummary? today,
        MeterDaySummary? yesterday,
        CancellationToken cancellationToken)
    {
        var key = GridMeterSensors.DirectionKey(direction);

        if (yesterday is not null)
        {
            await mqttPublisher.PublishAsync($"{key}/yesterday", GridMeterSensors.BuildDayPayload(yesterday), cancellationToken);
        }

        if (today is not null)
        {
            await mqttPublisher.PublishAsync($"{key}/today", GridMeterSensors.BuildDayPayload(today), cancellationToken);
        }

        if (today is not null
            && yesterday is not null
            && GridMeterSensors.SelectLatest(today, yesterday) is { } latest)
        {
            await mqttPublisher.PublishAsync($"{key}/latest", GridMeterSensors.BuildLatestPayload(latest), cancellationToken);
        }
    }

    private async Task<MeterDaySummary?> TryFetchAsync(
        MeterDirection direction,
        DayBounds bounds,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        try
        {
            var readings = await smartmeterClient.GetDayReadingsAsync(direction, bounds, cancellationToken);
            var summary = calculationService.Calculate(direction, bounds, readings);
            LogSummary(summary, timeZone);
            return summary;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning($"{direction} {bounds.Day:yyyy-MM-dd}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task TryPublishAsync(Func<Task> publish, CancellationToken cancellationToken)
    {
        try
        {
            await publish();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning($"MQTT publish failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LogSummary(MeterDaySummary summary, TimeZoneInfo timeZone)
    {
        var lastEnd = summary.LastIntervalEnd.HasValue
            ? TimeZoneInfo.ConvertTime(summary.LastIntervalEnd.Value, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture)
            : "n/a";

        logger.LogInformation(
            $"{summary.Direction} {summary.Day:yyyy-MM-dd}: {Format(summary.EnergyKwh)} kWh"
            + $" ({summary.IntervalCount}/{summary.ExpectedIntervalCount} intervals),"
            + $" last interval until {lastEnd}: {Format(summary.LastIntervalPowerW)} W,"
            + $" reading {Format(summary.LastReadingKwh)} kWh");
    }

    private static string Format(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
    }
}
