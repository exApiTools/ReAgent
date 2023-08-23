using System;
using System.Collections.Generic;

namespace ReAgent.State;

[Api]
public record SkillInfo([property: Api] bool Exists, [property: Api] string Name, [property: Api] bool CanBeUsed, [property: Api] int LifeCost, Lazy<List<MonsterInfo>> DeployedEntitiesFunc)
{
    [Api]
    public List<MonsterInfo> DeployedEntities => DeployedEntitiesFunc.Value;
    private Lazy<List<MonsterInfo>> DeployedEntitiesFunc { get; init; } = DeployedEntitiesFunc;
}
