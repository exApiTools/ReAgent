using System.Collections.Generic;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using Newtonsoft.Json;

namespace ReAgent;

public sealed class ReAgentSettings : ISettings
{
    public ReAgentSettings()
    {
        PluginSettings = new PluginSettings();
    }

    public readonly Dictionary<string, Profile> Profiles = new();
    public string CurrentProfile = string.Empty;

    public ToggleNode ShowDebugWindow { get; set; } = new(false);
    public ToggleNode InspectState { get; set; } = new(false);

    [JsonIgnore]
    [Menu(null, "To clipboard")]
    public ButtonNode DumpState { get; set; } = new();

    [IgnoreMenu]
    public RangeNode<int> GlobalKeyPressCooldown { get; set; } = new(200, 0, 1000);

    [IgnoreMenu]
    public RangeNode<int> MaximumMonsterRange { get; set; } = new(200, 0, 500);

    [IgnoreMenu]
    public RangeNode<int> HistorySecondsToKeep { get; set; } = new(60, 0, 600);

    public TextNode ImageDirectory { get; set; } = new("textures/ReAgent");
    private PluginSettings _pluginSettings;

    public PluginSettings PluginSettings
    {
        get => _pluginSettings;
        set
        {
            value.Parent = this;
            _pluginSettings = value;
        }
    }

    public ToggleNode Enable { get; set; } = new(true);
}

[Submenu(CollapsedByDefault = true)]
public class PluginSettings
{
    internal ReAgentSettings Parent;

    public RangeNode<int> GlobalKeyPressCooldown
    {
        get => Parent.GlobalKeyPressCooldown;
        set => Parent.GlobalKeyPressCooldown = value;
    }

    public RangeNode<int> MaximumMonsterRange
    {
        get => Parent.MaximumMonsterRange;
        set => Parent.MaximumMonsterRange = value;
    }

    public RangeNode<int> HistorySecondsToKeep
    {
        get => Parent.HistorySecondsToKeep;
        set => Parent.HistorySecondsToKeep = value;
    }

    public ToggleNode EnableInEscapeState { get; set; } = new ToggleNode(false);
    public ToggleNode KeepEnableTogglesOnASingleLine { get; set; } = new(true);
    public ToggleNode ColorEnableToggles { get; set; } = new(true);
    public ToggleNode EnableVerticalGroupTabs { get; set; } = new(true);

    public RangeNode<int> VerticalTabContainerWidth { get; set; } = new(150, 0, 1000);
}