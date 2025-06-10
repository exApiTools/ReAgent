using System;
using System.Collections.Generic;

namespace ReAgent.State;

[Api]
public record SkillInfo(
    [property: Api] string Name,
    ushort Id,
    ushort Id2,
    [property: Api] bool Exists,
    [property: Api] bool CanBeUsed,
    [property: Api] bool IsUsing,
    [property: Api] int UseStage,
    [property: Api] int ManaCost,
    [property: Api] int LifeCost,
    [property: Api] int EsCost,
    [property: Api] int MaxUses,
    [property: Api] float MaxCooldown,
    [property: Api] int RemainingUses,
    [property: Api] List<float> Cooldowns,
    [property: Api] float CastTime,
    Lazy<List<MonsterInfo>> DeployedEntitiesFunc)
{
    [Api]
    public List<MonsterInfo> DeployedEntities => DeployedEntitiesFunc.Value;
    private Lazy<List<MonsterInfo>> DeployedEntitiesFunc { get; init; } = DeployedEntitiesFunc;

    public static SkillInfo Empty(string name) => new SkillInfo(name, 0, 0, false, false, false, 0, 0, 0, 0, 0, 0f, 0, [], 0f, new Lazy<List<MonsterInfo>>([]));
}
