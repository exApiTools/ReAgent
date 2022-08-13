using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace ReAgent.State;

[Api]
public class MonsterInfo
{
    private readonly Entity _entity;
    private bool? _isInvincible;

    public MonsterInfo(Entity entity)
    {
        _entity = entity;
    }

    [Api]
    public bool IsInvincible => _isInvincible ??= _entity.Stats?.GetValueOrDefault(GameStat.CannotBeDamaged) switch { 0 or null => false, _ => true };

    [Api]
    public MonsterRarity Rarity => _entity.Rarity switch
    {
        ExileCore.Shared.Enums.MonsterRarity.White => MonsterRarity.Normal,
        ExileCore.Shared.Enums.MonsterRarity.Magic => MonsterRarity.Magic,
        ExileCore.Shared.Enums.MonsterRarity.Rare => MonsterRarity.Rare,
        ExileCore.Shared.Enums.MonsterRarity.Unique => MonsterRarity.Unique,
        _ => MonsterRarity.Normal
    };

    public BuffDictionary Buffs => new BuffDictionary(_entity.GetComponent<Buffs>()?.BuffsList ?? new List<Buff>());

    public float Distance => _entity.DistancePlayer;
}

public class NearbyMonsterInfo
{
    private readonly SortedDictionary<int, List<MonsterInfo>> _monsters;

    public NearbyMonsterInfo(ReAgent plugin)
    {
        _monsters = new SortedDictionary<int, List<MonsterInfo>>();
        if (!plugin.GameController.Player.HasComponent<Render>())
        {
            return;
        }

        foreach (var entity in plugin.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster])
        {
            if (entity.DistancePlayer > plugin.Settings.MaximumMonsterRange ||
                !entity.HasComponent<Monster>() ||
                !entity.HasComponent<Positioned>() ||
                !entity.HasComponent<Render>() ||
                !entity.TryGetComponent<Buffs>(out var buffs) ||
                buffs.HasBuff("hidden_monster") ||
                !entity.IsHostile ||
                !entity.HasComponent<Life>() ||
                !entity.IsAlive ||
                !entity.HasComponent<ObjectMagicProperties>())
            {
                continue;
            }

            var distance = (int)Math.Ceiling(entity.DistancePlayer);
            if (_monsters.TryGetValue(distance, out var list))
            {
                list.Add(new MonsterInfo(entity));
            }
            else
            {
                _monsters[distance] = new List<MonsterInfo> { new MonsterInfo(entity) };
            }
        }
    }

    public int GetMonsterCount(int range, MonsterRarity rarity) => GetMonsters(range, rarity).Count();

    public IEnumerable<MonsterInfo> GetMonsters(int range, MonsterRarity rarity) =>
        _monsters.TakeWhile(x => x.Key <= range).SelectMany(x => x.Value).Where(x => (x.Rarity & rarity) != 0);
}