namespace Trader.Application.Abstractions.Reporting;

public interface IMlOutcomePieChartPngRenderer
{
    byte[] Render(int correct, int wrong, int pending);
}
