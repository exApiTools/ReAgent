using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ImGuiNET;
using ReAgent.State;
using Vector4 = System.Numerics.Vector4;

namespace ReAgent;

public class ApiAttribute : Attribute
{

}

public class Profile
{
    private static int _lastTemporaryId = -1;
    private int _deleteIndex = -1;

    internal int TemporaryId { get; } = Interlocked.Increment(ref _lastTemporaryId);
    public List<RuleGroup> Groups { get; } = new();

    private string GetNewRuleGroupName()
    {
        return Enumerable.Range(0, 10000).Select(i => $"New rule group {i}").Except(Groups.Select(x => x.Name)).First();
    }

    public static Profile CreateWithDefaultGroup()
    {
        var profile = new Profile();
        profile.Groups.Add(new RuleGroup(profile.GetNewRuleGroupName()));
        return profile;
    }

    public void DrawSettings(RuleState state)
    {
        if (ImGui.BeginTabBar("Rule groups", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
            {
                Groups.Add(new RuleGroup(GetNewRuleGroupName()));
            }

            for (var i = 0; i < Groups.Count; i++)
            {
                var group = Groups[i];
                var preserveItem = true;
                if (!group.Enabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0, 1));
                }

                var isSelected = ImGui.BeginTabItem(
                    $"[{(group.EnabledInMaps ? "M" : "")}{(group.EnabledInTown ? "T" : "")}{(group.EnabledInHideout ? "H" : "")}]{group.Name}###RuleGroup{i}",
                    ref preserveItem,
                    ImGuiTabItemFlags.UnsavedDocument);

                if (!group.Enabled)
                {
                    ImGui.PopStyleColor();
                }

                if (ImGui.BeginDragDropSource())
                {
                    ImguiExt.SetDragDropPayload("RuleGroupIndex", i);
                    ImGui.Text(group.Name);
                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    var sourceId = ImguiExt.AcceptDragDropPayload<int>("RuleGroupIndex");
                    if (sourceId != null)
                    {
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            MoveGroup(sourceId.Value, i);
                        }
                    }

                    ImGui.EndDragDropTarget();
                }

                if (isSelected)
                {
                    group.DrawSettings(state);
                    ImGui.EndTabItem();
                }

                if (!preserveItem)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    {
                        Groups.RemoveAt(i);
                    }
                    else
                    {
                        _deleteIndex = i;
                        ImGui.OpenPopup("RuleGroupDeleteConfirmation");
                    }
                }
            }

            if (ImguiExt.DrawDeleteConfirmationPopup("RuleGroupDeleteConfirmation", _deleteIndex == -1 ? null : Groups[_deleteIndex].Name) == true)
            {
                Groups.RemoveAt(_deleteIndex);
                _deleteIndex = -1;
            }

            ImGui.EndTabBar();
        }
    }

    private void MoveGroup(int sourceIndex, int targetIndex)
    {
        var movedItem = Groups[sourceIndex];
        Groups.RemoveAt(sourceIndex);
        Groups.Insert(targetIndex, movedItem);
    }
}