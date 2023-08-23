using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record ResetFlagSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        return state.InternalState.CurrentGroupState.Flags.Remove(Id)
            ? SideEffectApplicationResult.AppliedUnique
            : SideEffectApplicationResult.AppliedDuplicate;
    }

    public override string ToString() => $"Reset flag {Id}";
}