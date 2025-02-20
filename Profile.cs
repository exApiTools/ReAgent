using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using Newtonsoft.Json;
using ReAgent.State;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ReAgent;

public class ApiAttribute : Attribute
{
}

public class Profile
{
    private static int _lastTemporaryId = -1;
    private int _deleteIndex = -1;
    private int _selectedGroupIndex = 0;

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

    private string _groupImportInput;
    private Task<(string text, bool edited)> _groupImportObject;

    private void DrawGroupImport()
    {
        var windowVisible = _groupImportInput != null;
        if (windowVisible)
        {
            if (ImGui.Begin("Import reagent group", ref windowVisible))
            {
                if (_groupImportObject is { IsCompleted: false })
                {
                    ImGui.Text("Checking...");
                }

                if (_groupImportObject is { IsFaulted: true })
                {
                    ImGui.Text($"Check failed: {string.Join("\n", _groupImportObject.Exception.InnerExceptions)}");
                }

                if (ImGui.InputText("Exported code", ref _groupImportInput, 20000))
                {
                    _groupImportObject = Task.Run(() =>
                    {
                        var data = DataExporter.ImportDataBase64(_groupImportInput, "reagent_group_v1");
                        data.ToObject<Profile>();
                        return (data.ToString(), false);
                    });
                }

                if (_groupImportObject is { IsCompletedSuccessfully: true })
                {
                    if (_groupImportObject.Result.edited)
                    {
                        ImGui.TextColored(Color.Green.ToImguiVec4(), "Editing manually");
                    }

                    var text = _groupImportObject.Result.text;
                    if (ImGui.InputTextMultiline("Json", ref text, 20000,
                            new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetContentRegionAvail().Y - 50, 50))))
                    {
                        _groupImportObject = Task.FromResult((text, true));
                    }
                }

                ImGui.BeginDisabled(_groupImportObject is not { IsCompletedSuccessfully: true });
                if (ImGui.Button("Import"))
                {
                    Groups.Add(JsonConvert.DeserializeObject<RuleGroup>(_groupImportObject.Result.text));
                    windowVisible = false;
                }

                ImGui.EndDisabled();
                ImGui.End();
            }

            if (!windowVisible)
            {
                _groupImportInput = null;
                _groupImportObject = null;
            }
        }
    }

    private unsafe void DrawSettingsVertical(RuleState state, ReAgentSettings settings)
    {
        if (ImGui.BeginChild("left", new Vector2(settings.PluginSettings.VerticalTabContainerWidth.Value, 0)))
        {
            bool popupRequested = false;
            for (var i = 0; i < Groups.Count; i++)
            {
                ImGui.PushID($"tab{i}");
                var group = Groups[i];
                if (!group.Enabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0, 1));
                }

                var selected = i == _selectedGroupIndex;
                var tabColor = *ImGui.GetStyleColorVec4(selected ? ImGuiCol.TabActive : ImGuiCol.Tab);
                var tabActiveColor = *ImGui.GetStyleColorVec4(ImGuiCol.TabActive);
                var tabHoverColor = *ImGui.GetStyleColorVec4(ImGuiCol.TabHovered);
                ImGui.PushStyleColor(ImGuiCol.Button, tabColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tabHoverColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, tabActiveColor);
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
                var tabClicked = ImGui.Button(
                    $"[{(group.EnabledInMaps ? "M" : "")}{(group.EnabledInTown ? "T" : "")}{(group.EnabledInHideout ? "H" : "")}{(group.EnabledInPeacefulAreas ? "P" : "")}]{group.Name}###RuleGroup{i}",
                    new Vector2(ImGui.GetWindowWidth() - ImGui.GetTextLineHeightWithSpacing(), ImGui.GetFrameHeight()));
                var tabHovered = ImGui.IsItemHovered();
                ImGui.PopStyleVar(1);
                ImGui.PopStyleColor(3);

                if (tabClicked)
                {
                    selected = true;
                }

                if (selected)
                {
                    _selectedGroupIndex = i;
                }

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

                ImGui.SameLine(0, 0);
                ImGui.PushStyleColor(ImGuiCol.Button, tabHovered ? tabHoverColor : tabColor);
                //ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                var closeButtonClicked = ImGui.Button("x", new Vector2(ImGui.GetTextLineHeightWithSpacing(), ImGui.GetFrameHeight()));
                //ImGui.PopStyleVar();
                ImGui.PopStyleColor(1);
                if (closeButtonClicked)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    {
                        Groups.RemoveAt(i);
                    }
                    else
                    {
                        _deleteIndex = i;
                        popupRequested = true;
                    }
                }

                ImGui.PopID();
            }

            if (ImGui.Button("+"))
            {
                Groups.Add(new RuleGroup(GetNewRuleGroupName()));
            }

            if (ImGui.Button("Import group"))
            {
                _groupImportInput = "";
                _groupImportObject = null;
            }

            if (popupRequested)
            {
                ImGui.OpenPopup("RuleGroupDeleteConfirmation");
            }

            var popupResult = ImguiExt.DrawDeleteConfirmationPopup("RuleGroupDeleteConfirmation", _deleteIndex == -1 ? null : Groups[_deleteIndex].Name);
            if (popupResult == true)
            {
                Groups.RemoveAt(_deleteIndex);
                _deleteIndex = -1;
            }
            else if (popupResult == false)
            {
                _deleteIndex = -1;
            }

            ImGui.EndChild();
        }

        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.BeginChild("item");
        if (Groups.Count > _selectedGroupIndex)
        {
            Groups[_selectedGroupIndex].DrawSettings(state, settings);
        }

        ImGui.EndChild();
        ImGui.EndGroup();
    }

    public void DrawSettings(RuleState state, ReAgentSettings settings)
    {
        if (settings.PluginSettings.EnableVerticalGroupTabs)
        {
            DrawSettingsVertical(state, settings);
        }
        else
        {
            DrawSettingsHorizontal(state, settings);
        }

        DrawGroupImport();
    }

    public void FocusLost()
    {
        _groupImportInput = null;
        _groupImportObject = null;
    }

    private void DrawSettingsHorizontal(RuleState state, ReAgentSettings settings)
    {
        if (ImGui.BeginTabBar("Rule groups", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
            {
                Groups.Add(new RuleGroup(GetNewRuleGroupName()));
            }

            if (ImGui.TabItemButton("Import", ImGuiTabItemFlags.Trailing))
            {
                _groupImportInput = "";
                _groupImportObject = null;
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
                    $"[{(group.EnabledInMaps ? "M" : "")}{(group.EnabledInTown ? "T" : "")}{(group.EnabledInHideout ? "H" : "")}{(group.EnabledInPeacefulAreas ? "P" : "")}]{group.Name}###RuleGroup{i}",
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
                    group.DrawSettings(state, settings);
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