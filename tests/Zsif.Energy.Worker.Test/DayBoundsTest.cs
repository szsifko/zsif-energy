using Microsoft.Extensions.Time.Testing;
using Zsif.Energy.Worker.Models;

namespace Zsif.Energy.Worker.Test;

public class DayBoundsTest
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    [Fact]
    public void TestCreateWinterDay()
    {
        var bounds = DayBounds.Create(new DateOnly(2026, 11, 8), Vienna);

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(1)), bounds.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(1)), bounds.To);
        Assert.Equal(96, bounds.ExpectedIntervalCount);
    }

    [Fact]
    public void TestCreateSummerDay()
    {
        var bounds = DayBounds.Create(new DateOnly(2026, 7, 1), Vienna);

        Assert.Equal(TimeSpan.FromHours(2), bounds.From.Offset);
        Assert.Equal(96, bounds.ExpectedIntervalCount);
    }

    [Fact]
    public void TestCreateSpringDstDayHas92Intervals()
    {
        var bounds = DayBounds.Create(new DateOnly(2026, 3, 29), Vienna);

        Assert.Equal(TimeSpan.FromHours(1), bounds.From.Offset);
        Assert.Equal(TimeSpan.FromHours(2), bounds.To.Offset);
        Assert.Equal(92, bounds.ExpectedIntervalCount);
    }

    [Fact]
    public void TestCreateAutumnDstDayHas100Intervals()
    {
        var bounds = DayBounds.Create(new DateOnly(2026, 10, 25), Vienna);

        Assert.Equal(TimeSpan.FromHours(2), bounds.From.Offset);
        Assert.Equal(TimeSpan.FromHours(1), bounds.To.Offset);
        Assert.Equal(100, bounds.ExpectedIntervalCount);
    }

    [Fact]
    public void TestTodayUsesViennaDateAfterUtcMidnightShift()
    {
        //23:30 UTC is already the next day in Vienna
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 9), DayBounds.Today(timeProvider, Vienna));
    }
}
