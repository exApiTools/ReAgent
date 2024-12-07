using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore2;
using ExileCore2.Shared.Enums;

namespace ReAgent.State;

[Api]
public class FlasksInfo
{
    private const int FlaskCount = 5;

    [Api]
    public FlaskInfo this[int i]
    {
        get
        {
            if (i < 0 || i >= FlaskCount)
            {
                throw new Exception($"Flask index is 0-based and must be in the range of 0-{FlaskCount - 1}");
            }

            return _flasks[i];
        }
    }

    [Api]
    public FlaskInfo Flask1 => this[0];

    [Api]
    public FlaskInfo Flask2 => this[1];

    [Api]
    public FlaskInfo Flask3 => this[2];

    [Api]
    public FlaskInfo Flask4 => this[3];

    [Api]
    public FlaskInfo Flask5 => this[4];

    private readonly List<FlaskInfo> _flasks;

    public FlasksInfo(GameController controller, RuleInternalState internalState)
    {
        var flaskInventory = controller.IngameState.ServerData.PlayerInventories.LastOrDefault(x => x.TypeId == InventoryNameE.Flask1);
        var flaskItems = Enumerable.Range(0, FlaskCount).Select(i => flaskInventory?.Inventory?[i, 0]).ToList();
        _flasks = flaskItems
            .Select((f,i) => FlaskInfo.From(controller, flaskItems, f, i, internalState))
            .ToList();
    }
}