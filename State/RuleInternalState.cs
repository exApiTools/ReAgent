using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
using Newtonsoft.Json;
using static ExileCore2.Shared.Nodes.HotkeyNodeV2;

namespace ReAgent.State;

public class RuleInternalState
{
    public bool CanPressKey { get; set; }
    public HotkeyNodeValue? KeyToPress { get; set; }
    public List<HotkeyNodeValue> KeysToHoldDown { get; set; } = [];
    public List<HotkeyNodeValue> KeysToRelease { get; set; } = [];
    public List<(string GraphicFilePath, Vector2 Position, Vector2 Size, string TintColor)> GraphicToDisplay { get; } = new();
    public List<(string Text, Vector2 Position, string Color)> TextToDisplay { get; } = new();
    public List<(string Text, Vector2 Position, Vector2 Size, float Fraction, string Color, string BackgroundColor, string TextColor)> ProgressBarsToDisplay { get; } = new();
    public bool AccessForbidden { get; set; }
    public RuleGroup CurrentGroup { get; private set; }
    public Dictionary<int, (bool WasActive, DateTime DeactivationTime)> TinctureUsageTracker { get; } = [];

    public bool ChatTitlePanelVisible { get; set; }

    public bool LeftPanelVisible { get; set; }
    public bool RightPanelVisible { get; set; }
    public bool FullscreenPanelVisible { get; set; }
    public bool LargePanelVisible { get; set; }

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