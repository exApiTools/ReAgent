using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public class BuffDictionary
{
    private readonly SkillDictionary _playerSkills;
    private readonly Dictionary<string, Buff> _source;
    private readonly List<Buff> _listSource;
    private readonly Lazy<List<StatusEffect>> _allBuffs;

    public BuffDictionary(List<Buff> source, SkillDictionary playerSkills)
    {
        _playerSkills = playerSkills;
        _listSource = source.Where(x => x.Name != null).ToList();
        _source = _listSource.DistinctBy(x => x.Name).ToDictionary(x => x.Name);
        _allBuffs = new Lazy<List<StatusEffect>>(() => _listSource.Select(CreateStatusEffect).ToList(), LazyThreadSafetyMode.None);
    }

    [Api]
    public StatusEffect this[string id]
    {
        get
        {
            if (_source.TryGetValue(id, out var value))
            {
                return CreateStatusEffect(value);
            }

            return new StatusEffect("", false, 0, 0, 0, new Lazy<SkillInfo>(() => SkillInfo.Empty("")));
        }
    }

    private StatusEffect CreateStatusEffect(Buff value)
    {
        return new StatusEffect(value.Name, true, value.Timer, value.MaxTime, value.BuffCharges, new Lazy<SkillInfo>(() =>
            Entity.Player.Equals(value.SourceEntity)
                ? _playerSkills?.ByNumericId(value.SourceSkillId, value.SourceSkillId2) ?? SkillInfo.Empty("")
                : SkillInfo.Empty("")));
    }

    /// <summary>Checks if there is a buff with name <paramref name="id"/></summary>
    [Api]
    public bool Has(string id)
    {
        return _source.ContainsKey(id);
    }

    public List<StatusEffect> AllBuffs => _allBuffs.Value;
}