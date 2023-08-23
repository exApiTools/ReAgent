using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Numerics;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record ProgressBarSideEffect(string Text, Vector2 Position, Vector2 Size, float Fraction, string Color, string BackgroundColor, string TextColor) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.ProgressBarsToDisplay.Add((Text, Position, Size, Fraction, Color, BackgroundColor, TextColor));
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Display \"{Text}\" at {Position} with color {Color}";
}