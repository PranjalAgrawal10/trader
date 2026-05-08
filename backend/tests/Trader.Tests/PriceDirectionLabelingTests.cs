using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class PriceDirectionLabelingTests
{
    [Fact]
    public void Classify_with_zero_threshold_matches_sign_only()
    {
        Assert.True(PriceDirectionLabeling.ClassifySignedLabel(100m, 101m, 0m) == 1);
        Assert.True(PriceDirectionLabeling.ClassifySignedLabel(100m, 99m, 0m) == -1);
        Assert.True(PriceDirectionLabeling.ClassifySignedLabel(100m, 100m, 0m) == 0);
    }

    [Fact]
    public void Classify_with_band_buckets_small_moves_as_neutral()
    {
        const decimal t = 0.001m; // ±0.1%
        Assert.True(PriceDirectionLabeling.ClassifySignedLabel(10_000m, 10_005m, t) == 0);
        Assert.True(PriceDirectionLabeling.ClassifySignedLabel(10_000m, 10_020m, t) == 1);
    }

    [Fact]
    public void OutcomeResolver_respects_threshold_for_actual_direction()
    {
        const decimal thresh = 0.001m;
        Assert.Equal(
            "wrong",
            PriceDirectionOutcomeResolver.Resolve("up", 10_000m, 10_005m, thresh));

        Assert.Equal(
            "correct",
            PriceDirectionOutcomeResolver.Resolve("neutral", 10_000m, 10_005m, thresh));
    }
}
