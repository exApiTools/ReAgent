using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
public record SetFlagSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.CurrentGroupState.Flags[Id] = true;
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Set flag {Id}";
}