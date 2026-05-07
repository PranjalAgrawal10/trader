namespace Trader.Application.Abstractions.Reporting;

public interface IMlOutcomePieChartPngRenderer
{
    /// <param name="chartTitle">Optional title rendered above the chart; default matches historic “favorites” reports.</param>
    byte[] Render(int correct, int wrong, int pending, string? chartTitle = null);
}
