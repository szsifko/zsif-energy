namespace Zsif.Energy.Worker.Models;

//The values endpoint returns an array of these series
public sealed class MeterSeries
{
    public string? Name { get; set; }

    public List<MeterReading> Data { get; set; } = [];
}
