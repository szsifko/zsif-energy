using Zsif.Energy.Worker.Models;
using Zsif.Energy.Worker.Services;

namespace Zsif.Energy.Worker.Test;

public class MeterCalculationServiceTest
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private static readonly DayBounds Day = DayBounds.Create(new DateOnly(2026, 11, 8), Vienna);

    private readonly MeterCalculationService _service = new();

    [Fact]
    public void TestCalculateSumsFullDayAndRoundsFloatingNoise()
    {
        var readings = Enumerable.Range(0, 96)
            .Select(i => Interval(i, 0.0080000000004m))
            .ToList();

        var summary = _service.Calculate(MeterDirection.Export, Day, readings);

        Assert.Equal(0.768m, summary.EnergyKwh);
        Assert.Equal(96, summary.IntervalCount);
        Assert.Equal(96, summary.ExpectedIntervalCount);
        Assert.Equal(Day.To, summary.LastIntervalEnd);
    }

    [Fact]
    public void TestCalculatePowerFromLastInterval()
    {
        var readings = new List<MeterReading>
        {
            Interval(0, 0.5m),
            Interval(1, 0.073m, 39044.254m)
        };

        var summary = _service.Calculate(MeterDirection.Import, Day, readings);

        Assert.Equal(292m, summary.LastIntervalPowerW);
        Assert.Equal(39044.254m, summary.LastReadingKwh);
        Assert.Equal(Day.From.AddMinutes(30), summary.LastIntervalEnd);
    }

    [Fact]
    public void TestCalculateIgnoresNullValues()
    {
        var readings = new List<MeterReading>
        {
            Interval(0, 0.1m),
            Interval(1, null),
            Interval(2, 0.2m),
            Interval(3, null)
        };

        var summary = _service.Calculate(MeterDirection.Import, Day, readings);

        Assert.Equal(0.3m, summary.EnergyKwh);
        Assert.Equal(2, summary.IntervalCount);
        Assert.Equal(Day.From.AddMinutes(45), summary.LastIntervalEnd);
    }

    [Fact]
    public void TestCalculateReturnsNullInsteadOfZeroWithoutData()
    {
        var readings = new List<MeterReading> { Interval(0, null) };

        var summary = _service.Calculate(MeterDirection.Import, Day, readings);

        Assert.Null(summary.EnergyKwh);
        Assert.Null(summary.LastIntervalPowerW);
        Assert.Null(summary.LastIntervalEnd);
        Assert.Null(summary.LastReadingKwh);
        Assert.Equal(0, summary.IntervalCount);
    }

    [Fact]
    public void TestCalculateDoesNotCountDuplicateIntervalsTwice()
    {
        var readings = new List<MeterReading>
        {
            Interval(0, 0.1m),
            Interval(0, 0.1m),
            Interval(1, 0.2m)
        };

        var summary = _service.Calculate(MeterDirection.Export, Day, readings);

        Assert.Equal(0.3m, summary.EnergyKwh);
        Assert.Equal(2, summary.IntervalCount);
    }

    [Fact]
    public void TestCalculateIgnoresIntervalsOutsideDay()
    {
        var readings = new List<MeterReading>
        {
            Interval(-1, 5m),
            Interval(0, 0.1m),
            Interval(96, 5m)
        };

        var summary = _service.Calculate(MeterDirection.Export, Day, readings);

        Assert.Equal(0.1m, summary.EnergyKwh);
        Assert.Equal(1, summary.IntervalCount);
    }

    [Fact]
    public void TestCalculateAutumnDstDay()
    {
        var bounds = DayBounds.Create(new DateOnly(2026, 10, 25), Vienna);
        var readings = Enumerable.Range(0, 100)
            .Select(i => new MeterReading
            {
                StartTimestamp = bounds.From.AddMinutes(i * 15),
                EndTimestamp = bounds.From.AddMinutes((i + 1) * 15),
                Value = 0.01m
            })
            .ToList();

        var summary = _service.Calculate(MeterDirection.Import, bounds, readings);

        Assert.Equal(1.0m, summary.EnergyKwh);
        Assert.Equal(100, summary.IntervalCount);
        Assert.Equal(100, summary.ExpectedIntervalCount);
    }

    private static MeterReading Interval(int index, decimal? value, decimal? reading = null)
    {
        return new MeterReading
        {
            StartTimestamp = Day.From.AddMinutes(index * 15),
            EndTimestamp = Day.From.AddMinutes((index + 1) * 15),
            Value = value,
            Reading = reading
        };
    }
}
