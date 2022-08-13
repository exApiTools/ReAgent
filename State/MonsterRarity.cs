using System;

namespace ReAgent.State;

[Flags]
[Api]
public enum MonsterRarity
{
    [Api] Normal = 1 << 0,
    [Api] Magic = 1 << 1,
    [Api] Rare = 1 << 2,
    [Api] Unique = 1 << 3,
    [Api] Any = Normal | Magic | Rare | Unique,
    [Api] AtLeastRare = Rare | Unique
}