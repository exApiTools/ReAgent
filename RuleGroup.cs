using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using ReAgent.SideEffects;
using ReAgent.State;
using SharpDX;

namespace ReAgent;

public class RuleGroup
{
    private bool _expand;
    private int _deleteIndex = -1;

    public List<Rule> Rules = new();
    public bool Enabled;
    public bool EnabledInTown;
    public bool EnabledInHideout;
    public bool EnabledInMaps = true;
    public string Name;

    public RuleGroup(string name)
    {
        Name = name;
    }

    public void DrawSettings(RuleState state)
    {
        ImGui.Checkbox("Enable", ref Enabled);
        ImGui.Checkbox("Enable in town", ref EnabledInTown);
        ImGui.Checkbox("Enable in hideout", ref EnabledInHideout);
        ImGui.Checkbox("Enable in maps", ref EnabledInMaps);
        ImGui.InputText("Name", ref Name, 20);

        if (Rules.Any() && ImGui.Button($"{(_expand ? "Collapse" : "Expand")}###ExpandHideButton"))
        {
            _expand = !_expand;
        }

        using var groupReg = state?.InternalState.SetCurrentGroup(this);
        for (var i = 0; i < Rules.Count; i++)
        {
            ImGui.PushID($"Rule{i}");
            if (i != 0)
            {
                ImGui.Separator();
            }

            var dropTargetStart = ImGui.GetCursorPos();
            ImGui.PushStyleColor(ImGuiCol.Button, 0);
            ImGui.Button("=");
            ImGui.PopStyleColor();
            ImGui.SameLine();

            if (ImGui.BeginDragDropSource())
            {
                ImguiExt.SetDragDropPayload("RuleIndex", i);
                Rules[i].Display(state, false);
                ImGui.EndDragDropSource();
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Drag me");
            }

            if (ImGui.Button("Delete"))
            {
                if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                {
                    RemoveAt(i);
                    ImGui.PopID();
                    break;
                }

                _deleteIndex = i;
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hold Shift to delete without confirmation");
            }

            ImGui.SameLine();
            Rules[i].Display(state, _expand);
            ImguiExt.DrawLargeTransparentSelectable("##DragTarget", dropTargetStart);
            if (ImGui.BeginDragDropTarget())
            {
                var sourceId = ImguiExt.AcceptDragDropPayload<int>("RuleIndex");
                if (sourceId != null)
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        MoveRule(sourceId.Value, i);
                    }
                }

                ImGui.EndDragDropTarget();
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add New Rule"))
        {
            Rules.Add(new Rule("false"));
        }

        if (ImGui.TreeNode("State"))
        {
            var flags = state.InternalState.CurrentGroupState.Flags;
            var timers = state.InternalState.CurrentGroupState.Timers;
            var numbers = state.InternalState.CurrentGroupState.Numbers;
            if ((flags.Any() || timers.Any() || numbers.Any()) && ImGui.Button("Clear"))
            {
                flags.Clear();
                timers.Clear();
                numbers.Clear();
            }

            ImGui.Text("Timers:");
            foreach (var (name, timer) in timers)
            {
                ImGui.TextColored(timer.IsRunning ? Color.Green.ToImguiVec4() : Color.Yellow.ToImguiVec4(), $"{name}: {timer.Elapsed.TotalSeconds}");
            }

            ImGui.Text("Flags:");
            foreach (var (name, flag) in flags)
            {
                ImGui.TextColored(flag ? Color.Green.ToImguiVec4() : Color.Yellow.ToImguiVec4(), name);
            }

            ImGui.Text("Numbers:");
            foreach (var (name, value) in flags)
            {
                ImGui.TextColored(Color.Green.ToImguiVec4(), $"{name}: {value}");
            }

            ImGui.TreePop();
        }

        if (_deleteIndex != -1)
        {
            ImGui.OpenPopup("RuleDeleteConfirmation");
        }

        var deleteResult = ImguiExt.DrawDeleteConfirmationPopup("RuleDeleteConfirmation", _deleteIndex == -1 ? null : $"rule with index {_deleteIndex}");
        if (deleteResult == true)
        {
            RemoveAt(_deleteIndex);
        }

        if (deleteResult != null)
        {
            _deleteIndex = -1;
        }
    }

    public IEnumerable<SideEffectContainer> Evaluate(RuleState state)
    {
        if (Enabled &&
            (state.IsInHideout, state.IsInTown) switch
            {
                (true, _) => EnabledInHideout,
                (_, true) => EnabledInTown,
                (false, false) => EnabledInMaps,
            })
        {
            using var groupReg = state.InternalState.SetCurrentGroup(this);
            foreach (var rule in Rules)
            {
                using var ruleReg = state.InternalState.CurrentGroupState.SetCurrentRule(rule);
                foreach (var effect in rule.Evaluate(state))
                {
                    yield return new SideEffectContainer(effect, this, rule);
                }
            }
        }
    }

    private void RemoveAt(int index)
    {
        Rules.RemoveAt(index);
    }

    private void MoveRule(int sourceIndex, int targetIndex)
    {
        var movedItem = Rules[sourceIndex];
        Rules.RemoveAt(sourceIndex);
        Rules.Insert(targetIndex, movedItem);
    }
}