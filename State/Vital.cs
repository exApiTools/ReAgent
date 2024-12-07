using GameOffsets2;

namespace ReAgent.State;

[Api]
public record Vital([property: Api] double Current, [property: Api] double Max)
{
    [Api]
    public double Percent => Current / Max * 100;

    public static Vital From(VitalStruct vital)
    {
        return new Vital(vital.Current, vital.Unreserved);
    }
}