namespace ReAgent.State;

[Api]
public record SkillInfo([property: Api] bool Exists, [property: Api] string Name, [property: Api] bool CanBeUsed);