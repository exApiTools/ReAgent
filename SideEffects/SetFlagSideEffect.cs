using System.Collections.Generic;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record SetFlagSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        if (state.InternalState.CurrentGroupState.Flags.GetValueOrDefault(Id) == true)
        {
            return SideEffectApplicationResult.AppliedDuplicate;
        }

        state.InternalState.CurrentGroupState.Flags[Id] = true;
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Set flag {Id}";
}