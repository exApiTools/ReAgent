using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;

namespace ReAgent.State;

[Api]
public record FlaskInfo(
    [property: Api] bool Active,
    [property: Api] bool CanBeUsed,
    [property: Api] int Charges,
    [property: Api] int MaxCharges,
    [property: Api] int ChargesPerUse,
    [property: Api] string Name,
    [property: Api] float CanBeUsedIn)
{
    public static FlaskInfo From(
        GameController state,
        List<ServerInventory.InventSlotItem> flaskItems,
        ServerInventory.InventSlotItem flaskItem,
        int index,
        RuleInternalState internalState)
    {
        if (flaskItem?.Address is 0 or null || flaskItem.Item?.Address is null or 0)
        {
            return new FlaskInfo(false, false, 0, 1, 1, "", 100);
        }

        var active = false;
        var canBeUsedIn = 0f;
        bool canbeUsed = false;
        var chargeComponent = flaskItem.Item.GetComponent<Charges>();
        if (state.Player.TryGetComponent<Buffs>(out var playerBuffs))
        {
            if (flaskItem.Item.TryGetComponent<Flask>(out var flask))
            {
                var buffNames = GetFlaskBuffNames(flask);
                active = buffNames.Any(playerBuffs.HasBuff);
                canbeUsed = (chargeComponent?.NumCharges ?? 0) >= (chargeComponent?.ChargesPerUse ?? 1);
            }
        }


        var name = "";
        if (flaskItem.Item.TryGetComponent<Base>(out var baseC))
        {
            name = baseC.Name;
        }

        if (flaskItem.Item.TryGetComponent<Mods>(out var mods) && name != "")
        {
            name = mods.UniqueName;
        }

        return new FlaskInfo(active, canbeUsed, chargeComponent?.NumCharges ?? 0, chargeComponent?.ChargesMax ?? 1, chargeComponent?.ChargesPerUse ?? 1, name, canBeUsedIn);
    }

    private static readonly string[] LifeFlaskBuffs = { "flask_effect_life" };

    private static readonly string[] ManaFlaskBuffs =
    {
        "flask_effect_mana",
        "flask_effect_mana_not_removed_when_full",
        "flask_instant_mana_recovery_at_end_of_effect"
    };

    private static IEnumerable<string> GetFlaskBuffNames(Flask flask)
    {
        var type = flask.M.Read<int>(flask.Address + 0x28, 0x10);
        return type switch
        {
            1 => LifeFlaskBuffs,
            2 => ManaFlaskBuffs,
            3 => LifeFlaskBuffs.Concat(ManaFlaskBuffs),
            4 when flask.M.ReadStringU(flask.M.Read<long>(flask.Address + 0x28, 0x18, 0x0)) is { } s and not "" => new[] { s },
            _ => Enumerable.Empty<string>()
        };
    }
}