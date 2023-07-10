using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore.PoEMemory.Components;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public class SkillDictionary
{
    private readonly Lazy<Dictionary<string, SkillInfo>> _source;

    public SkillDictionary(Actor actor, Life lifeComponent)
    {
        if (actor == null)
        {
            _source = new Lazy<Dictionary<string, SkillInfo>>(() => new Dictionary<string, SkillInfo>(), LazyThreadSafetyMode.None);
        }
        else
        {
            _source = new Lazy<Dictionary<string, SkillInfo>>(() => actor.ActorSkills
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new SkillInfo(true, x.Name, x.CanBeUsed && x.Cost <= (lifeComponent?.CurMana ?? 10000), x.GetStat(ExileCore.Shared.Enums.GameStat.LifeCost)))
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

            return new SkillInfo(false, id, false, 0);
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
