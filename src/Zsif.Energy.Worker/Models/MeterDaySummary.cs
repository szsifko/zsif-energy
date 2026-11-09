namespace Zsif.Energy.Worker.Models;

public sealed record MeterDaySummary(
    MeterDirection Direction,
    DateOnly Day,
    decimal? EnergyKwh,
    int IntervalCount,
    int ExpectedIntervalCount,
    decimal? LastIntervalPowerW,
    DateTimeOffset? LastIntervalEnd,
    decimal? LastReadingKwh);
