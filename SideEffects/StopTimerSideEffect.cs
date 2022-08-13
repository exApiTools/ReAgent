using System.Collections.Generic;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
public record StopTimerSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.CurrentGroupState.Timers.GetValueOrDefault(Id)?.Stop();
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Stop timer {Id}";
}