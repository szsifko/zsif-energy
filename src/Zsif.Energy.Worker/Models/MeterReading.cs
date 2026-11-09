namespace Zsif.Energy.Worker.Models;

public sealed class MeterReading
{
    public DateTimeOffset StartTimestamp { get; set; }

    public DateTimeOffset EndTimestamp { get; set; }

    //Energy of the interval in kWh, null when not yet delivered
    public decimal? Value { get; set; }

    //Meter reading in kWh at the end of the interval
    public decimal? Reading { get; set; }

    public string? Status { get; set; }
}
