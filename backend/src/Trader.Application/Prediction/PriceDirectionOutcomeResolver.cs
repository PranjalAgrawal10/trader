namespace Trader.Application.Prediction;

public static class PriceDirectionOutcomeResolver
{
    public static string Resolve(string direction, decimal refClose, decimal nextClose)
    {
        var actual = nextClose > refClose ? "up" : nextClose < refClose ? "down" : "neutral";
        if (direction == "neutral")
            return actual == "neutral" ? "correct" : "wrong";
        if (direction == "up")
            return actual == "up" ? "correct" : actual == "down" ? "wrong" : "wrong";
        if (direction == "down")
            return actual == "down" ? "correct" : actual == "up" ? "wrong" : "wrong";
        return "wrong";
    }
}
