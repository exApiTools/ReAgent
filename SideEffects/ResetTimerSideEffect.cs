using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
public record ResetTimerSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.CurrentGroupState.Timers.TryRemove(Id, out _);
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Reset timer {Id}";
}