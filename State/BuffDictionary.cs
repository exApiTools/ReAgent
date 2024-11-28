using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public class BuffDictionary
{
    private readonly SkillDictionary _playerSkills;
    private readonly Dictionary<string, Buff> _source;
    private readonly Dictionary<string, Buff> _exerts;

    public BuffDictionary(List<Buff> source, SkillDictionary playerSkills)
    {
        _playerSkills = playerSkills;
        _source = source.Where(x => x.Name != null).DistinctBy(x => x.Name).ToDictionary(x => x.Name);
        _exerts = source.Where(x => x.Name is "display_num_empowered_attacks").ToDictionary(x => x.SourceSkill.Name);
    }

    [Api]
    public StatusEffect this[string id]
    {
        get
        {
            if (_source.TryGetValue(id, out var value))
            {
                return new StatusEffect(true, value.Timer, value.MaxTime, value.BuffCharges, new Lazy<SkillInfo>(() =>
                    Entity.Player.Equals(value.SourceEntity)
                        ? _playerSkills?.ByNumericId(value.SourceSkillId, value.SourceSkillId2) ?? SkillInfo.Empty("")
                        : SkillInfo.Empty("")));
            }

            return new StatusEffect(false, 0, 0, 0, new Lazy<SkillInfo>(() => SkillInfo.Empty("")));
        }
    }

    /// <summary>Checks if there is a buff with name <paramref name="id"/></summary>
    [Api]
    public bool Has(string id)
    {
        return _source.ContainsKey(id);
    }

    /// <summary>Gets the number of exerted attacks remaining from a skill <paramref name="id"/></summary>
    [Api]
    public int ExertedAttacks(string id)
    {
        if (_exerts.TryGetValue(id, out var value))
        {
            return value.BuffCharges;
        }
        return 0;
    }

    [JsonProperty]
    private Dictionary<string, StatusEffect> AllBuffs => _source.Keys.ToDictionary(x => x, x => this[x]);
}