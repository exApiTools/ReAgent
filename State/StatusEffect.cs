namespace ReAgent.State;

[Api]
public record StatusEffect([property: Api] bool Exists, [property: Api] double TimeLeft, [property: Api] double TotalTime, [property: Api] int Charges)
{
    [Api]
    public double PercentTimeLeft =>
        Exists
            ? double.IsPositiveInfinity(TimeLeft)
                ? 100
                : 100 * TimeLeft / TotalTime
            : 0;
}