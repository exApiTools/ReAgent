using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
public record ResetFlagSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.CurrentGroupState.Flags.Remove(Id);
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Reset flag {Id}";
}