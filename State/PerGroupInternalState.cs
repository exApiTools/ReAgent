using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace ReAgent.State;

public class PerGroupInternalState
{
    public Rule CurrentRule { get; private set; }
    public ConcurrentDictionary<string, Stopwatch> Timers { get; } = new();
    public Dictionary<string, bool> Flags { get; } = new();
    public Dictionary<string, float> Numbers { get; } = new();
    public Dictionary<Rule, Stopwatch> ConditionActivations { get; } = new();

    public IDisposable SetCurrentRule(Rule group)
    {
        return new RuleRegistration(this, group);
    }

    private class RuleRegistration : IDisposable
    {
        public RuleRegistration(PerGroupInternalState state, Rule rule)
        {
            _state = state;
            _oldRule = _state.CurrentRule;
            _state.CurrentRule = rule;
        }

        public void Dispose()
        {
            _state.CurrentRule = _oldRule;
        }

        private readonly PerGroupInternalState _state;
        private readonly Rule _oldRule;
    }
}