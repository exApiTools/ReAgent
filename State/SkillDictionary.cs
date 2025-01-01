using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;

namespace ReAgent.State;

[Api]
public class SkillDictionary
{
    private readonly Lazy<Dictionary<string, SkillInfo>> _source;
    private readonly Lazy<Actor> _actor;
    private readonly Lazy<PoolInfo> _poolInfo;
    private readonly GameController _controller;
    private readonly Lazy<List<SkillInfo>> _allSkills;

    private record struct PoolInfo(int ManaPool, int HpPoll, int EsPool);

    public SkillDictionary(GameController controller, Entity entity, bool isActiveSkillSet)
    {
        _controller = controller;
        _actor = new Lazy<Actor>(() => entity?.GetComponent<Actor>(), LazyThreadSafetyMode.None);

        _poolInfo = new Lazy<PoolInfo>(() =>
        {
            var lifeComponent = entity?.GetComponent<Life>();
            if (lifeComponent == null)
            {
                return new PoolInfo(10000, 10000, 10000);
            }

            var currentManaPool = lifeComponent.CurMana;
            var currentHpPool = lifeComponent.CurHP;
            var currentEsPool = lifeComponent.CurES;
            return new PoolInfo(currentManaPool, currentHpPool, currentEsPool);
        }, LazyThreadSafetyMode.None);

        _source = new Lazy<Dictionary<string, SkillInfo>>(() =>
        {
            var actor = _actor.Value;
            if (actor == null)
            {
                return [];
            }

            var stats = entity.GetComponent<Stats>();
            var statsActiveSetIndex = stats?.ActiveWeaponSetIndex;
            var poolInfo = _poolInfo.Value;
            var skills = actor.ActorSkills.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToLookup(x => x.Name, StringComparer.OrdinalIgnoreCase);
            return skills
                .Select(x => x.FirstOrDefault(s => (s.WeaponSetBinding == null || statsActiveSetIndex == null || statsActiveSetIndex == s.WeaponSetBinding) == isActiveSkillSet) ??
                             x.First())
                .Select(x => CreateSkillInfo(x, controller, poolInfo.ManaPool, poolInfo.HpPoll, poolInfo.EsPool))
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }, LazyThreadSafetyMode.None);

        _allSkills = new Lazy<List<SkillInfo>>(() =>
        {
            var actor = _actor.Value;
            if (actor == null)
            {
                return [];
            }

            var poolInfo = _poolInfo.Value;
            return actor.ActorSkills
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => CreateSkillInfo(x, controller, poolInfo.ManaPool, poolInfo.HpPoll, poolInfo.EsPool))
                .OrderBy(x => x.Name).ToList();
        }, LazyThreadSafetyMode.None);
    }

    private static SkillInfo CreateSkillInfo(ActorSkill skill, GameController controller, int currentManaPool, int currentHpPool, int currentEsPool)
    {
        return new SkillInfo(skill.Name,
            skill.Id,
            skill.Id2,
            true,
            skill.CanBeUsed &&
            skill.CanBeUsedWithWeapon &&
            skill.Cost <= currentManaPool &&
            skill.LifeCost <= currentHpPool &&
            skill.EsCost <= currentEsPool,
            skill.IsUsing,
            skill.SkillUseStage,
            skill.Cost,
            skill.LifeCost,
            skill.EsCost,
            skill.CooldownInfo?.MaxUses ?? 1,
            skill.Cooldown,
            skill.RemainingUses,
            skill.CooldownInfo?.SkillCooldowns.Select(c => c.Remaining).ToList() ?? [],
            (float)skill.CastTime.TotalSeconds,
            new Lazy<List<MonsterInfo>>(() => skill.DeployedObjects.Select(d => d?.Entity)
                    .Where(e => e != null)
                    .Select(e => new MonsterInfo(controller, e))
                    .ToList(),
                LazyThreadSafetyMode.None));
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

    public SkillInfo Current => _actor.Value.CurrentAction switch
    {
        null => SkillInfo.Empty(""), { Skill: { } skill } => CreateSkillInfo(skill, _controller, _poolInfo.Value.ManaPool, _poolInfo.Value.HpPoll, _poolInfo.Value.EsPool)
    };

    public SkillInfo ByNumericId(int id, int id2) => _source.Value.Values.FirstOrDefault(x => x.Id == id && x.Id2 == id2);

    [Api]
    public bool Has(string id)
    {
        return _source.Value.ContainsKey(id);
    }

    public List<SkillInfo> AllSkills => _allSkills.Value;
}