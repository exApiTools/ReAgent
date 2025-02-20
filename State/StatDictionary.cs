using System.Collections.Generic;
using ExileCore.Shared.Enums;
using Newtonsoft.Json;

namespace ReAgent.State;

[Api]
public record Stat([property: Api] bool Exists, [property: Api] int Value);

[Api]
public class StatDictionary
{
    private readonly Dictionary<GameStat, int> _source;

    public StatDictionary(Dictionary<GameStat, int> source)
    {
        _source = source;
    }

    [Api]
    public Stat this[GameStat id]
    {
        get
        {
            if (_source.TryGetValue(id, out var value))
            {
                return new Stat(true, value);
            }

            return new Stat(false, 0);
        }
    }

    [Api]
    public bool Has(GameStat id)
    {
        return _source.ContainsKey(id);
    }

    [JsonProperty]
    private Dictionary<GameStat, int> AllStats => _source;
}