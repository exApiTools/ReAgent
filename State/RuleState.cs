using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;

namespace ReAgent.State;

[Api]
public class RuleState
{
    private readonly Lazy<NearbyMonsterInfo> _nearbyMonsterInfo;
    private readonly Lazy<List<EntityInfo>> _miscellaneousObjects;
    private RuleInternalState _internalState;
    private readonly Lazy<List<EntityInfo>> _effects;
    private readonly Lazy<List<MonsterInfo>> _allMonsters;

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
        set
        {
            if (_internalState is { AccessForbidden: true })
            {
                throw new Exception("Access denied");
            }

            _internalState = value;
        }
    }

    public RuleState(ReAgent plugin)
    {
        var controller = plugin.GameController;
        if (controller != null)
        {
            IsInHideout = plugin.GameController.Area.CurrentArea.IsHideout;
            IsInTown = plugin.GameController.Area.CurrentArea.IsTown;
            var player = controller.Player;
            if (player.TryGetComponent<Buffs>(out var playerBuffs))
            {
                Ailments = plugin.CustomAilments
                    .Where(x => x.Value.Any(playerBuffs.HasBuff))
                    .Select(x => x.Key)
                    .ToHashSet();
            }

            Buffs = new BuffDictionary(playerBuffs?.BuffsList ?? new List<Buff>());

            if (player.TryGetComponent<Life>(out var lifeComponent))
            {
                Vitals = new VitalsInfo(lifeComponent);
            }

            if (player.TryGetComponent<Actor>(out var actorComponent))
            {
                Animation = actorComponent.Animation;
                IsMoving = actorComponent.isMoving;
                Skills = new SkillDictionary(controller, player);
                AnimationId = actorComponent.AnimationController?.CurrentAnimationId ?? 0;
                AnimationStage = actorComponent.AnimationController?.CurrentAnimationStage ?? 0;
            }

            Flasks = new FlasksInfo(controller);
            Player = new MonsterInfo(controller, player);
            _nearbyMonsterInfo = new Lazy<NearbyMonsterInfo>(() => new NearbyMonsterInfo(plugin), LazyThreadSafetyMode.None);
            _miscellaneousObjects = new Lazy<List<EntityInfo>>(() =>
                controller.EntityListWrapper.ValidEntitiesByType[EntityType.MiscellaneousObjects].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _allMonsters = new Lazy<List<MonsterInfo>>(() =>
                controller.EntityListWrapper.ValidEntitiesByType[EntityType.Monster].Select(x => new MonsterInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
            _effects = new Lazy<List<EntityInfo>>(() =>
                controller.EntityListWrapper.ValidEntitiesByType[EntityType.Effect].Select(x => new EntityInfo(controller, x)).ToList(), LazyThreadSafetyMode.None);
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
    public SkillDictionary Skills { get; } = new SkillDictionary(null, null);

    [Api]
    public VitalsInfo Vitals { get; }

    [Api]
    public FlasksInfo Flasks { get; }

    [Api]
    public MonsterInfo Player { get; }
    public bool IsInHideout { get; }
    public bool IsInTown { get; }

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
    public IEnumerable<MonsterInfo> AllMonsters => _allMonsters.Value;

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