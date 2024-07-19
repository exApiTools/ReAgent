using System;
using System.Collections.Generic;

namespace ReAgent.State;

[Api]
public record SkillInfo(
    [property: Api] bool Exists,
    [property: Api] string Name,
    [property: Api] bool CanBeUsed,
    [property: Api] bool IsUsing,
    [property: Api] int UseStage,
    [property: Api] int ManaCost,
    [property: Api] int LifeCost,
    [property: Api] int EsCost,
    [property: Api] int MaxUses,
    [property: Api] int RemainingUses,
    [property: Api] List<float> Cooldowns,
    Lazy<List<MonsterInfo>> DeployedEntitiesFunc)
{
    [Api]
    public List<MonsterInfo> DeployedEntities => DeployedEntitiesFunc.Value;
    private Lazy<List<MonsterInfo>> DeployedEntitiesFunc { get; init; } = DeployedEntitiesFunc;
}
