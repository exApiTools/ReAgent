using System.Collections.Generic;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record SetNumberSideEffect(string Id, float Value) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (state.InternalState.CurrentGroupState.Numbers.GetValueOrDefault(Id) == Value)
        {
            return SideEffectApplicationResult.AppliedDuplicate;
        }

        state.InternalState.CurrentGroupState.Numbers[Id] = Value;
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Set number {Id} to {Value}";
}