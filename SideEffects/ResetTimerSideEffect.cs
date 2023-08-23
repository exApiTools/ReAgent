using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record ResetTimerSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        return state.InternalState.CurrentGroupState.Timers.TryRemove(Id, out _)
            ? SideEffectApplicationResult.AppliedUnique
            : SideEffectApplicationResult.AppliedDuplicate;
    }

    public override string ToString() => $"Reset timer {Id}";
}