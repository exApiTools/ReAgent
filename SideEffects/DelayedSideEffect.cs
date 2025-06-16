using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record DelayedSideEffect(double Delay, Func<IReadOnlyList<ISideEffect>> SideEffects) : ISideEffect
{
    public DelayedSideEffect(double Delay, IReadOnlyList<ISideEffect> SideEffects) : this(Delay, () => SideEffects)
    {
    }

    private readonly Stopwatch _sw = new Stopwatch();
    private readonly Dictionary<ISideEffect, bool> _states = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ISideEffect> _evaluatedSideEffects;

    public SideEffectApplicationResult Apply(RuleState state)
    {
        if (!_sw.IsRunning)
        {
            _sw.Start();
            return SideEffectApplicationResult.UnableToApply;
        }

        if (_sw.Elapsed.TotalSeconds > Delay)
        {
            _evaluatedSideEffects ??= SideEffects() ?? [];
            foreach (var sideEffect in _evaluatedSideEffects.Where(x => !_states.GetValueOrDefault(x)))
            {
                if (sideEffect.Apply(state) == SideEffectApplicationResult.UnableToApply)
                {
                    return SideEffectApplicationResult.UnableToApply;
                }

                _states[sideEffect] = true;
            }

            return SideEffectApplicationResult.AppliedUnique;
        }

        return SideEffectApplicationResult.UnableToApply;
    }

    public override string ToString() => $"Delayed ({Delay}s) invoke: {string.Join(", ", SideEffects())}";
}