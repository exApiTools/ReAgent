using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace ReAgent.State;

public class RuleInternalState
{
    public bool CanPressKey { get; set; }
    public Keys? KeyToPress { get; set; }
    public bool AccessForbidden { get; set; }
    public RuleGroup CurrentGroup { get; private set; }

    [JsonProperty]
    private Dictionary<RuleGroup, PerGroupInternalState> PerGroupStates { get; } = new();

    [JsonIgnore]
    public PerGroupInternalState CurrentGroupState =>
        PerGroupStates.TryGetValue(CurrentGroup, out var state)
            ? state
            : PerGroupStates[CurrentGroup] = new PerGroupInternalState();

    public IDisposable SetCurrentGroup(RuleGroup group)
    {
        return new RuleGroupRegistration(this, group);
    }

    private class RuleGroupRegistration : IDisposable
    {
        public RuleGroupRegistration(RuleInternalState state, RuleGroup group)
        {
            _state = state;
            _oldGroup = _state.CurrentGroup;
            _state.CurrentGroup = group;
        }

        public void Dispose()
        {
            _state.CurrentGroup = _oldGroup;
        }

        private readonly RuleInternalState _state;
        private readonly RuleGroup _oldGroup;
    }
}