using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;

namespace ReAgent.State;

[Api]
public class BuffDictionary
{
    private readonly Dictionary<string, Buff> _source;

    public BuffDictionary(List<Buff> source)
    {
        _source = source.DistinctBy(x => x.Name).ToDictionary(x => x.Name);
    }

    [Api]
    public StatusEffect this[string id]
    {
        get
        {
            if (_source.TryGetValue(id, out var value))
            {
                return new StatusEffect(true, value.Timer, value.MaxTime, value.BuffCharges);
            }

            return new StatusEffect(false, 0, 0, 0);
        }
    }

    /// <summary>Checks if there is a buff with name <paramref name="id"/></summary>
    [Api]
    public bool Has(string id)
    {
        return _source.ContainsKey(id);
    }
}