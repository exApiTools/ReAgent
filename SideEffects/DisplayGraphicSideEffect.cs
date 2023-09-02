using System.IO;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Numerics;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record DisplayGraphicSideEffect(string GraphicFilePath, Vector2 Position, Vector2 Size, string ColorTint) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.GraphicToDisplay.Add((GraphicFilePath, Position, Size, ColorTint));
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Display \"{GraphicFilePath}\" at {Position} with {Size} and colortint {ColorTint}";
}