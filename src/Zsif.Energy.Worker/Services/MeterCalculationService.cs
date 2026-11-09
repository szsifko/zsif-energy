using Zsif.Energy.Worker.Models;

namespace Zsif.Energy.Worker.Services;

public sealed class MeterCalculationService
{
    public MeterDaySummary Calculate(MeterDirection direction, DayBounds bounds, IEnumerable<MeterReading> readings)
    {
        //Only delivered intervals inside the day, one per start timestamp to avoid double counting
        var intervals = readings
            .Where(r => r.Value.HasValue
                && r.StartTimestamp >= bounds.From
                && r.EndTimestamp <= bounds.To
                && r.EndTimestamp > r.StartTimestamp)
            .GroupBy(r => r.StartTimestamp)
            .Select(g => g.Last())
            .OrderBy(r => r.StartTimestamp)
            .ToList();

        if (intervals.Count == 0)
        {
            return new MeterDaySummary(direction, bounds.Day, null, 0, bounds.ExpectedIntervalCount, null, null, null);
        }

        var last = intervals[^1];
        var energyKwh = Round(intervals.Sum(r => r.Value!.Value), 3);
        var lastHours = (decimal)(last.EndTimestamp - last.StartTimestamp).TotalHours;
        var lastPowerW = Round(last.Value!.Value / lastHours * 1000m, 0);
        var lastReading = last.Reading.HasValue ? Round(last.Reading.Value, 3) : (decimal?)null;

        return new MeterDaySummary(
            direction,
            bounds.Day,
            energyKwh,
            intervals.Count,
            bounds.ExpectedIntervalCount,
            lastPowerW,
            last.EndTimestamp,
            lastReading);
    }

    private static decimal Round(decimal value, int decimals)
    {
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }
}
