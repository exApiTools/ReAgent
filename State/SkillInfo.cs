using System;
using System.Collections.Generic;

namespace ReAgent.State;

[Api]
public record SkillInfo(
    ushort Id,
    ushort Id2,
    [property: Api] bool Exists,
    [property: Api] string Name,
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
    Lazy<List<MonsterInfo>> DeployedEntitiesFunc)
{
    [Api]
    public List<MonsterInfo> DeployedEntities => DeployedEntitiesFunc.Value;
    private Lazy<List<MonsterInfo>> DeployedEntitiesFunc { get; init; } = DeployedEntitiesFunc;

    public static SkillInfo Empty(string name) => new SkillInfo(0, 0, false, name, false, false, 0, 0, 0, 0, 0, 0f, 0, [], new Lazy<List<MonsterInfo>>([]));
}
