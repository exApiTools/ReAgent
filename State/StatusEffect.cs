using System;

namespace ReAgent.State;

[Api]
public record StatusEffect(
    [property: Api] string Name,
    [property: Api] string DisplayName,
    [property: Api] bool Exists,
    [property: Api] double TimeLeft,
    [property: Api] double TotalTime,
    [property: Api] int Charges,
    Lazy<SkillInfo> SkillInfoLazy)
{
    [Api]
    public double PercentTimeLeft =>
        Exists
            ? double.IsPositiveInfinity(TimeLeft)
                ? 100
                : 100 * TimeLeft / TotalTime
            : 0;

    [Api]
    public SkillInfo Skill => SkillInfoLazy.Value;
}