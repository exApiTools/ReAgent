using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using GameOffsets;
using SharpDX;

namespace ReAgent.State;

[Api]
public class EntityInfo
{
    protected readonly GameController Controller;
    protected readonly Entity Entity;
    private readonly Lazy<StatDictionary> _stats;
    private string _baseEntityPath;

    public EntityInfo(GameController controller, Entity entity)
    {
        Controller = controller;
        Entity = entity;
        _stats = new Lazy<StatDictionary>(() => new StatDictionary(Entity.Stats ?? new Dictionary<GameStat, int>()), LazyThreadSafetyMode.None);
    }

    [Api]
    public string Path => Entity.Path;

    [Api]
    public string BaseEntityPath => _baseEntityPath ??= Entity.GetComponent<Animated>()?.BaseAnimatedObjectEntity?.Path;

    [Api]
    public Vector3 Position => Entity.Pos;

    [Api]
    public Vector2 Position2D => Position switch { var p => new Vector2(p.X, p.Y) };

    [Api]
    public float DistanceToCursor => Controller.IngameState.ServerData.WorldMousePosition.WorldToGrid().Distance(Entity.GridPos);

    [Api]
    public StatDictionary Stats => _stats.Value;

    [Api]
    public bool IsAlive => Entity.IsAlive;
}

[Api]
public class MonsterInfo : EntityInfo
{
    private bool? _isInvincible;

    public MonsterInfo(GameController controller, Entity entity) : base(controller, entity)
    {
    }

    [Api]
    public bool IsInvincible => _isInvincible ??= Stats[GameStat.CannotBeDamaged].Value switch { 0 => false, _ => true };

    [Api]
    public MonsterRarity Rarity => Entity.Rarity switch
    {
        ExileCore.Shared.Enums.MonsterRarity.White => MonsterRarity.Normal,
        ExileCore.Shared.Enums.MonsterRarity.Magic => MonsterRarity.Magic,
        ExileCore.Shared.Enums.MonsterRarity.Rare => MonsterRarity.Rare,
        ExileCore.Shared.Enums.MonsterRarity.Unique => MonsterRarity.Unique,
        _ => MonsterRarity.Normal
    };

    [Api]
    public BuffDictionary Buffs => new BuffDictionary(Entity.GetComponent<Buffs>()?.BuffsList ?? new List<Buff>());

    [Api]
    public float Distance => Entity.DistancePlayer;
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
            var monsterInfo = new MonsterInfo(plugin.GameController, entity);
            if (_monsters.TryGetValue(distance, out var list))
            {
                list.Add(monsterInfo);
            }
            else
            {
                _monsters[distance] = new List<MonsterInfo> { monsterInfo };
            }
        }
    }

    public int GetMonsterCount(int range, MonsterRarity rarity) => GetMonsters(range, rarity).Count();

    public IEnumerable<MonsterInfo> GetMonsters(int range, MonsterRarity rarity) =>
        _monsters.TakeWhile(x => x.Key <= range).SelectMany(x => x.Value).Where(x => (x.Rarity & rarity) != 0);
}