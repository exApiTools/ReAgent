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
        if (!timer.IsRunning)
        {
            timer.Start();
            return SideEffectApplicationResult.AppliedUnique;
        }

        return SideEffectApplicationResult.AppliedDuplicate;
    }

    public override string ToString() => $"Start timer {Id}";
}