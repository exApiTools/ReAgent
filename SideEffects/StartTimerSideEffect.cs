using System.Diagnostics;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
public record StartTimerSideEffect(string Id) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        var timer = state.InternalState.CurrentGroupState.Timers.GetOrAdd(Id, _ => new Stopwatch());
        timer.Start();
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Start timer {Id}";
}