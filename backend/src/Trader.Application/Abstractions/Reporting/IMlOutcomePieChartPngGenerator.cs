namespace Trader.Application.Abstractions.Reporting;

/// <summary>Produces outcome pie visuals as PNG for email <c>&lt;img src="cid:…"&gt;</c> embedding.</summary>
public interface IMlOutcomePieChartPngGenerator
{
    byte[] RenderPng(int correct, int wrong, int pending);
}
