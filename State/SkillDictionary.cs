using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore;
using ExileCore.PoEMemory.Components;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public class SkillDictionary
{
    private readonly Lazy<Dictionary<string, SkillInfo>> _source;

public SkillDictionary(GameController controller, Actor actor, Life lifeComponent)
{
    if (actor == null)
    {
        _source = new Lazy<Dictionary<string, SkillInfo>>(new Dictionary<string, SkillInfo>());
    }
    else
    {
        var localPlayer = controller.Game.IngameState.Data.LocalPlayer;
        var eldritchBatteryTaken = localPlayer.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var value) && value > 0;

        _source = new Lazy<Dictionary<string, SkillInfo>>(() => actor.ActorSkills
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SkillInfo(true, x.Name, 
                x.CanBeUsed && x.Cost <= (eldritchBatteryTaken && lifeComponent != null ? lifeComponent?.CurES + lifeComponent.CurMana : lifeComponent?.CurMana ?? 10000), 
                x.GetStat(ExileCore.Shared.Enums.GameStat.LifeCost),
                new Lazy<List<MonsterInfo>>(() => x.DeployedObjects.Select(d => d?.Entity)
                        .Where(e => e != null)
                        .Select(e => new MonsterInfo(controller, e))
                        .ToList(),
                    LazyThreadSafetyMode.None)))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase), LazyThreadSafetyMode.None);
    }
}

    [Api]
    public SkillInfo this[string id]
    {
        get
        {
            if (_source.Value.TryGetValue(id, out var value))
            {
                return value;
            }

            return new SkillInfo(false, id, false, 0, new Lazy<List<MonsterInfo>>(new List<MonsterInfo>()));
        }
    }

    [Api]
    public bool Has(string id)
    {
        return _source.Value.ContainsKey(id);
    }

    [JsonProperty]
    private Dictionary<string, SkillInfo> AllSkills => _source.Value;
}
