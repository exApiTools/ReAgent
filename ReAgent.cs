using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using Newtonsoft.Json;
using ReAgent.SideEffects;
using ReAgent.State;
using Color = SharpDX.Color;

namespace ReAgent;

public sealed class ReAgent : BaseSettingsPlugin<ReAgentSettings>
{
    private readonly Queue<(DateTime Date, string Description)> _actionInfo = new();
    private readonly Stopwatch _sinceLastKeyPress = Stopwatch.StartNew();
    private readonly RuleInternalState _internalState = new RuleInternalState();
    private readonly ConditionalWeakTable<Profile, string> _pendingNames = new ConditionalWeakTable<Profile, string>();
    private readonly HashSet<string> _loadedTextures = new();
    private RuleState _state;
    private List<SideEffectContainer> _pendingSideEffects = new List<SideEffectContainer>();
    private string _profileToDelete = null;
    public Dictionary<string, List<string>> CustomAilments { get; set; } = new Dictionary<string, List<string>>();
    public static int ProcessID { get; private set; }

    public override bool Initialise()
    {
        ProcessID = GameController.Window.Process.Id;

        var stringData = File.ReadAllText(Path.Join(DirectoryFullName, "CustomAilments.json"));
        CustomAilments = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(stringData);
        Settings.DumpState.OnPressed = () =>
        {
            ImGui.SetClipboardText(JsonConvert.SerializeObject(new RuleState(this) { InternalState = _internalState }));
        };
        Settings.ImageDirectory.OnValueChanged = () =>
        {
            foreach (var loadedTexture in _loadedTextures)
            {
                Graphics.DisposeTexture(loadedTexture);
            }

            _loadedTextures.Clear();
        };
        return base.Initialise();
    }

    public override void DrawSettings()
    {
        base.DrawSettings();

        try
        {
            _state = new RuleState(this) { InternalState = _internalState };
        }
        catch (Exception ex)
        {
            LogError(ex.ToString());
        }

        if (ImGui.BeginTabBar("Profiles", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            if (ImGui.TabItemButton("+##addProfile", ImGuiTabItemFlags.Trailing))
            {
                var profileName = GetNewProfileName();
                Settings.Profiles.Add(profileName, Profile.CreateWithDefaultGroup());
            }

            foreach (var (profileName, profile) in Settings.Profiles.OrderByDescending(x => x.Key == Settings.CurrentProfile).ThenBy(x => x.Key).ToList())
            {
                var preserveItem = true;
                var isCurrentProfile = profileName == Settings.CurrentProfile;
                if (isCurrentProfile)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.LightGreen.ToImgui());
                }

                var tabSelected = ImGui.BeginTabItem($"{profileName}###{profile.TemporaryId}", ref preserveItem, ImGuiTabItemFlags.UnsavedDocument);
                if (isCurrentProfile)
                {
                    ImGui.PopStyleColor();
                }

                if (tabSelected)
                {
                    _pendingNames.TryGetValue(profile, out var newProfileName);
                    newProfileName ??= profileName;
                    ImGui.InputText("Name", ref newProfileName, 40);
                    if (!isCurrentProfile && ImGui.Button("Activate"))
                    {
                        Settings.CurrentProfile = profileName;
                    }

                    if (profileName != newProfileName)
                    {
                        if (Settings.Profiles.ContainsKey(newProfileName))
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(Color.Red.ToImguiVec4(), "This profile name is already used");
                            _pendingNames.AddOrUpdate(profile, newProfileName);
                        }
                        else
                        {
                            Settings.Profiles.Remove(profileName);
                            Settings.Profiles.Add(newProfileName, profile);
                            if (isCurrentProfile)
                            {
                                Settings.CurrentProfile = newProfileName;
                            }

                            _pendingNames.Clear();
                        }
                    }

                    profile.DrawSettings(_state);
                    ImGui.EndTabItem();
                }

                if (!preserveItem)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    {
                        Settings.Profiles.Remove(profileName);
                    }
                    else
                    {
                        _profileToDelete = profileName;
                        ImGui.OpenPopup("ProfileDeleteConfirmation");
                    }
                }
            }

            var deleteResult = ImguiExt.DrawDeleteConfirmationPopup("ProfileDeleteConfirmation", $"profile {_profileToDelete}");
            if (deleteResult == true)
            {
                Settings.Profiles.Remove(_profileToDelete);
            }

            if (deleteResult != null)
            {
                _profileToDelete = null;
            }

            ImGui.EndTabBar();
        }
    }

    private string GetNewProfileName()
    {
        return Enumerable.Range(1, 10000).Select(i => $"New profile {i}").Except(Settings.Profiles.Keys).First();
    }

    public override void Render()
    {
        if (Settings.Profiles.Count == 0)
        {
            Settings.Profiles.Add(GetNewProfileName(), Profile.CreateWithDefaultGroup());
            Settings.CurrentProfile = Settings.Profiles.Keys.Single();
        }

        if (string.IsNullOrEmpty(Settings.CurrentProfile) || !Settings.Profiles.TryGetValue(Settings.CurrentProfile, out var profile))
        {
            Settings.CurrentProfile = Settings.Profiles.Keys.First();
            profile = Settings.Profiles[Settings.CurrentProfile];
        }

        var shouldExecute = ShouldExecute(out var state);
        while (_actionInfo.TryPeek(out var entry) && (DateTime.Now - entry.Date).TotalSeconds > Settings.HistorySecondsToKeep)
        {
            _actionInfo.Dequeue();
        }

        if (Settings.ShowDebugWindow)
        {
            var show = Settings.ShowDebugWindow.Value;
            ImGui.Begin("Debug Mode Window", ref show);
            Settings.ShowDebugWindow.Value = show;
            ImGui.TextWrapped($"State: {state}");
            if (ImGui.Button("Clear History"))
            {
                _actionInfo.Clear();
            }

            ImGui.BeginChild("KeyPressesInfo");
            foreach (var (dateTime, @event) in _actionInfo.Reverse())
            {
                ImGui.TextUnformatted($"{dateTime:HH:mm:ss.fff}: {@event}");
            }

            ImGui.EndChild();
            ImGui.End();
        }

        if (!shouldExecute)
        {
            return;
        }

        _internalState.KeyToPress = null;
        _internalState.TextToDisplay.Clear();
        _internalState.GraphicToDisplay.Clear();
        _internalState.ProgressBarsToDisplay.Clear();
        _internalState.ChatTitlePanelVisible = GameController.IngameState.IngameUi.ChatTitlePanel.IsVisible;
        _internalState.CanPressKey = _sinceLastKeyPress.ElapsedMilliseconds >= Settings.GlobalKeyPressCooldown && !_internalState.ChatTitlePanelVisible;
        _internalState.LeftPanelVisible = GameController.IngameState.IngameUi.OpenLeftPanel.IsVisible;
        _internalState.RightPanelVisible = GameController.IngameState.IngameUi.OpenRightPanel.IsVisible;
        _internalState.LargePanelVisible = GameController.IngameState.IngameUi.LargePanels.Any(p => p.IsVisible);
        _internalState.FullscreenPanelVisible = GameController.IngameState.IngameUi.FullscreenPanels.Any(p => p.IsVisible);
        _state = new RuleState(this) { InternalState = _internalState };

        ApplyPendingSideEffects();

        foreach (var group in profile.Groups)
        {
            var newSideEffects = group.Evaluate(_state).ToList();
            foreach (var sideEffect in newSideEffects)
            {
                sideEffect.SetPending();
                _pendingSideEffects.Add(sideEffect);
            }
        }

        ApplyPendingSideEffects();

        if (_internalState.KeyToPress is { } key)
        {
            _internalState.KeyToPress = null;
            Input.KeyDown(key);
            Input.KeyUp(key);
            _sinceLastKeyPress.Restart();
        }

        foreach (var (text, position, size, fraction, color, backgroundColor, textColor) in _internalState.ProgressBarsToDisplay)
        {
            var textSize = Graphics.MeasureText(text);
            Graphics.DrawBox(position, position + size, ColorFromName(backgroundColor));
            Graphics.DrawBox(position, position + size with { X = size.X * fraction }, ColorFromName(color));
            Graphics.DrawText(text, position + size / 2 - textSize / 2, ColorFromName(textColor));
        }

        foreach (var (graphicFilePath, position, size, tintColor) in _internalState.GraphicToDisplay)
        {
            if (!_loadedTextures.Contains(graphicFilePath))
            {
                var graphicFileFullPath = Path.Combine(Path.GetDirectoryName(typeof(Core).Assembly.Location)!, Settings.ImageDirectory, graphicFilePath);
                if (File.Exists(graphicFileFullPath))
                {
                    if (Graphics.InitImage(graphicFilePath, graphicFileFullPath))
                    {
                        _loadedTextures.Add(graphicFilePath);
                    }
                }
            }

            if (_loadedTextures.Contains(graphicFilePath))
            {
                Graphics.DrawImage(graphicFilePath, new SharpDX.RectangleF(position.X, position.Y, size.X, size.Y), ColorFromName(tintColor));
            }
        }
        foreach (var (text, position, color) in _internalState.TextToDisplay)
        {
            var textSize = Graphics.MeasureText(text);
            Graphics.DrawBox(position, position + textSize, Color.Black);
            Graphics.DrawText(text, position, ColorFromName(color));
        }
    }

    private static Color ColorFromName(string color)
    {
        return System.Drawing.Color.FromName(color) switch { var c => new Color(c.R, c.G, c.B, c.A) };
    }

    private void ApplyPendingSideEffects()
    {
        var applicationResults = _pendingSideEffects.Select(x => (x, ApplicationResult: x.Apply(_state))).ToList();
        foreach (var successfulApplication in applicationResults.Where(x =>
                     x.ApplicationResult is SideEffectApplicationResult.AppliedUnique or SideEffectApplicationResult.AppliedDuplicate))
        {
            successfulApplication.x.SetExecuted(_state);
            if (successfulApplication.ApplicationResult == SideEffectApplicationResult.AppliedUnique)
            {
                _actionInfo.Enqueue((DateTime.Now, successfulApplication.x.SideEffect.ToString()));
            }
        }

        _pendingSideEffects = applicationResults.Where(x => x.ApplicationResult == SideEffectApplicationResult.UnableToApply).Select(x => x.x).ToList();
    }


    private bool ShouldExecute(out string state)
    {
        if (!GameController.Window.IsForeground())
        {
            state = "Game window is not focused";
            return false;
        }

        if (GameController.Player.TryGetComponent<Life>(out var lifeComp))
        {
            if (lifeComp.CurHP <= 0)
            {
                state = "Player is dead";
                return false;
            }
        }
        else
        {
            state = "Cannot find player Life component";
            return false;
        }

        if (GameController.Player.TryGetComponent<Buffs>(out var buffComp))
        {
            if (buffComp.HasBuff("grace_period"))
            {
                state = "Grace period is active";
                return false;
            }
        }
        else
        {
            state = "Cannot find player Buffs component";
            return false;
        }

        if (!GameController.Player.HasComponent<Actor>())
        {
            state = "Cannot find player Actor component";
            return false;
        }

        state = "Ready";
        return true;
    }
}