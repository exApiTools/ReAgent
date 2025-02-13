using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.Shared.Enums;

namespace ReAgent.State;

[Api]
public class RuleState
{
    private readonly Lazy<NearbyMonsterInfo> _nearbyMonsterInfo;
    private readonly Lazy<List<EntityInfo>> _miscellaneousObjects;
    private readonly Lazy<List<EntityInfo>> _noneEntities;
    private readonly RuleInternalState _internalState;
    private readonly Lazy<List<EntityInfo>> _ingameiconObjects;
    private readonly Lazy<List<EntityInfo>> _miniMonoliths;

    private readonly Lazy<List<EntityInfo>> _effects;
    private readonly Lazy<List<MonsterInfo>> _allMonsters;
    private readonly Lazy<List<MonsterInfo>> _hiddenMonsters;
    private readonly Lazy<List<MonsterInfo>> _allPlayers;
    private readonly Lazy<List<MonsterInfo>> _corpses;

    public RuleInternalState InternalState
    {
        get
        {
            if (_internalState.AccessForbidden)
            {
                throw new Exception("Access denied");
            }

            return _internalState;
        }
    }

    public RuleState(ReAgent plugin, RuleInternalState internalState)
    {
        _internalState = internalState;
        var controller = plugin.GameController;
        if (controller != null)
        {
            IsInHideout = plugin.GameController.Area.CurrentArea.IsHideout;
            IsInTown = plugin.GameController.Area.CurrentArea.IsTown;
            IsInPeacefulArea = plugin.GameController.Area.CurrentArea.IsPeaceful;
            IsInEscapeMenu = plugin.GameController.Game.IsEscapeState;
            AreaName = plugin.GameController.Area.CurrentArea.Name;

            var player = controller.Player;
            if (player.TryGetComponent<Buffs>(out var playerBuffs))
            {
                Ailments = plugin.CustomAilments
                    .Where(x => x.Value.Any(playerBuffs.HasBuff))
                    .Select(x => x.Key)
                    .ToHashSet();
            }

            if (player.TryGetComponent<Stats>(out var stats))
            {
                ActiveWeaponSetIndex = stats.ActiveWeaponSetIndex;
            }

            if (player.TryGetComponent<Life>(out var lifeComponent))
            {
                Vitals = new VitalsInfo(lifeComponent);
            }

            if (player.TryGetComponent<Actor>(out var actorComponent))
            {
                Animation = actorComponent.Animation;
                IsMoving = actorComponent.isMoving;
                Skills = new SkillDictionary(controller, player, true);
                WeaponSwapSkills = new SkillDictionary(controller, player, false);
                AnimationId = actorComponent.AnimationController?.CurrentAnimationId ?? 0;
                AnimationStage = actorComponent.AnimationController?.CurrentAnimationStage ?? 0;
            }

            Buffs = new BuffDictionary(playerBuffs?.BuffsList ?? [], Skills);

            Flasks = new FlasksInfo(controller, InternalState);
            Player = new MonsterInfo(controller, player);
            _nearbyMonsterInfo = new Lazy<NearbyMonsterInfo>(() => new NearbyMonsterInfo(plugin), LazyThreadSafetyMode.None);
            _miscellaneousObjects = new Lazy<List<EntityInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.MiscellaneousObjects].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _noneEntities = new Lazy<List<EntityInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.None].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _ingameiconObjects = new Lazy<List<EntityInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.IngameIcon].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _miniMonoliths = new Lazy<List<EntityInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.MiniMonolith].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _allMonsters = new Lazy<List<MonsterInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(e => NearbyMonsterInfo.IsValidMonster(plugin, e, false, false))
                    .Select(x => new MonsterInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _hiddenMonsters = new Lazy<List<MonsterInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(e => NearbyMonsterInfo.IsValidMonster(plugin, e, false, true))
                    .Select(x => new MonsterInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _corpses = new Lazy<List<MonsterInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(e => NearbyMonsterInfo.IsValidMonster(plugin, e, false, false))
                .Where(x => x.IsDead)
                    .Select(x => new MonsterInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _effects = new Lazy<List<EntityInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.Effect].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _allPlayers = new Lazy<List<MonsterInfo>>(() => controller.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                    .Select(x => new MonsterInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
        }
    }


    [Api]
    public bool IsMoving { get; }

    [Api]
    public BuffDictionary Buffs { get; }

    [Api]
    public AnimationE Animation { get; }

    [Api]
    public int AnimationId { get; }

    [Api]
    public int AnimationStage { get; }

    [Api]
    public IReadOnlyCollection<string> Ailments { get; } = new List<string>();

    [Api]
    public int ActiveWeaponSetIndex { get; }

    [Api]
    public SkillDictionary Skills { get; } = new SkillDictionary(null, null, true);

    [Api]
    public SkillDictionary WeaponSwapSkills { get; } = new SkillDictionary(null, null, false);

    [Api]
    public VitalsInfo Vitals { get; }

    [Api]
    public FlasksInfo Flasks { get; }

    [Api]
    public MonsterInfo Player { get; }

    [Api]
    public bool IsInHideout { get; }

    [Api]
    public bool IsInTown { get; }

    [Api]
    public bool IsInPeacefulArea { get; }

    [Api]
    public bool IsInEscapeMenu { get; }

    [Api]
    public string AreaName { get; }

    [Api]
    public int MonsterCount(int range, MonsterRarity rarity) => _nearbyMonsterInfo.Value.GetMonsterCount(range, rarity);

    [Api]
    public int MonsterCount(int range) => MonsterCount(range, MonsterRarity.Any);

    [Api]
    public int MonsterCount() => MonsterCount(int.MaxValue);

    [Api]
    public IEnumerable<MonsterInfo> Monsters(int range, MonsterRarity rarity) => _nearbyMonsterInfo.Value.GetMonsters(range, rarity);

    [Api]
    public IEnumerable<MonsterInfo> FriendlyMonsters => _nearbyMonsterInfo.Value.FriendlyMonsters;

    [Api]
    public IEnumerable<MonsterInfo> Monsters(int range) => Monsters(range, MonsterRarity.Any);

    [Api]
    public IEnumerable<MonsterInfo> Monsters() => Monsters(int.MaxValue);

    [Api]
    public IEnumerable<EntityInfo> MiscellaneousObjects => _miscellaneousObjects.Value;

    [Api]
    public IEnumerable<EntityInfo> NoneEntities => _noneEntities.Value;

    [Api]
    public IEnumerable<EntityInfo> IngameIcons => _ingameiconObjects.Value;

    [Api]
    public IEnumerable<EntityInfo> MiniMonoliths => _miniMonoliths.Value;

    [Api]
    public IEnumerable<MonsterInfo> AllMonsters => _allMonsters.Value;

    [Api]
    public IEnumerable<MonsterInfo> HiddenMonsters => _hiddenMonsters.Value;

    [Api]
    public IEnumerable<MonsterInfo> Corpses => _corpses.Value;

    [Api]
    public IEnumerable<MonsterInfo> AllPlayers => _allPlayers.Value;

    [Api]
    public MonsterInfo PlayerByName(string name) => _allPlayers.Value.FirstOrDefault(p => p.PlayerName.Equals(name));

    [Api]
    public IEnumerable<EntityInfo> Effects => _effects.Value;

    [Api]
    public bool IsKeyPressed(Keys key) => Input.IsKeyDown(key);

    [Api]
    public bool SinceLastActivation(double minTime) =>
        (_internalState.CurrentGroupState.ConditionActivations.GetValueOrDefault(_internalState.CurrentGroupState.CurrentRule)?.Elapsed.TotalSeconds ??
         double.PositiveInfinity) > minTime;

    [Api]
    public bool IsFlagSet(string name) => _internalState.CurrentGroupState.Flags.GetValueOrDefault(name);

    [Api]
    public float GetNumberValue(string name) => _internalState.CurrentGroupState.Numbers.GetValueOrDefault(name);

    [Api]
    public float GetTimerValue(string name) => (float?)_internalState.CurrentGroupState.Timers.GetValueOrDefault(name)?.Elapsed.TotalSeconds ?? 0f;

    [Api]
    public bool IsTimerRunning(string name) => _internalState.CurrentGroupState.Timers.GetValueOrDefault(name)?.IsRunning ?? false;

    [Api]
    public bool IsChatOpen => _internalState.ChatTitlePanelVisible;

    [Api]
    public bool IsLeftPanelOpen => _internalState.LeftPanelVisible;

    [Api]
    public bool IsRightPanelOpen => _internalState.RightPanelVisible;

    [Api]
    public bool IsAnyFullscreenPanelOpen => _internalState.FullscreenPanelVisible;

    [Api]
    public bool IsAnyLargePanelOpen => _internalState.LargePanelVisible;
}