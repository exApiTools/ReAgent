using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public class SkillDictionary
{
    private readonly Lazy<Dictionary<string, SkillInfo>> _source;

    public SkillDictionary(GameController controller, Entity entity)
    {
        var actor = entity?.GetComponent<Actor>();
        var lifeComponent = entity?.GetComponent<Life>();
        if (actor == null)
        {
            _source = new Lazy<Dictionary<string, SkillInfo>>([]);
        }
        else
        {
            var eldritchBatteryTaken = entity.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var value) && value > 0;
            var currentManaPool = lifeComponent == null
                ? 10000
                : eldritchBatteryTaken
                    ? lifeComponent.CurES + lifeComponent.CurMana
                    : lifeComponent.CurMana;
            var currentHpPool = lifeComponent == null
                ? 10000
                : eldritchBatteryTaken
                    ? lifeComponent.CurHP
                    : lifeComponent.CurES + lifeComponent.CurHP;

            var currentEsPool = lifeComponent?.CurES ?? 10000;

            _source = new Lazy<Dictionary<string, SkillInfo>>(() => actor.ActorSkills
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new SkillInfo(
                    x.Id,
                    x.Id2,
                    true,
                    x.Name,
                    x.CanBeUsed &&
                    x.CanBeUsedWithWeapon &&
                    x.Cost <= currentManaPool &&
                    x.LifeCost <= currentHpPool &&
                    x.EsCost <= currentEsPool,
                    x.IsUsing,
                    x.SkillUseStage,
                    x.Cost,
                    x.LifeCost,
                    x.EsCost,
                    x.CooldownInfo?.MaxUses ?? 1,
                    x.RemainingUses,
                    x.CooldownInfo?.SkillCooldowns.Select(c => c.Remaining).ToList() ?? [],
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

            return SkillInfo.Empty(id);
        }
    }

    public SkillInfo ByNumericId(int id, int id2) => _source.Value.Values.FirstOrDefault(x => x.Id == id && x.Id2 == id2);

    [Api]
    public bool Has(string id)
    {
        return _source.Value.ContainsKey(id);
    }

    [JsonProperty]
    private Dictionary<string, SkillInfo> AllSkills => _source.Value;
}