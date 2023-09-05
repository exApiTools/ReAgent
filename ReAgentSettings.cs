using System.Collections.Generic;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using Newtonsoft.Json;

namespace ReAgent;

public sealed class ReAgentSettings : ISettings
{
    public readonly Dictionary<string, Profile> Profiles = new();
    public string CurrentProfile = string.Empty;

    public ToggleNode ShowDebugWindow { get; set; } = new ToggleNode(false);

    [JsonIgnore]
    public ButtonNode DumpState { get; set; } = new ButtonNode();

    public RangeNode<int> GlobalKeyPressCooldown { get; set; } = new RangeNode<int>(200, 0, 1000);
    public RangeNode<int> MaximumMonsterRange { get; set; } = new RangeNode<int>(200, 0, 500);
    public RangeNode<int> HistorySecondsToKeep { get; set; } = new RangeNode<int>(60, 0, 600);
    public TextNode ImageDirectory { get; set; } = new TextNode("textures/ReAgent");

    public ToggleNode Enable { get; set; } = new ToggleNode(true);
}