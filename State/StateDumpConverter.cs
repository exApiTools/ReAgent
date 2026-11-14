using System;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using Newtonsoft.Json;

namespace ReAgent.State;

/// <summary>
/// Keeps the "Dump state" output to ReAgent's own state. Game memory objects (ItemMod, Entity,
/// components...) link back to TheGame, so serializing them walks the whole game and loops.
/// They are written as short summaries instead, and delegates are skipped.
/// </summary>
public class StateDumpConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType) =>
        typeof(Delegate).IsAssignableFrom(objectType) ||
        typeof(RemoteMemoryObject).IsAssignableFrom(objectType) ||
        // Backstop for memory-backed types that don't derive from RemoteMemoryObject.
        (objectType.IsClass && objectType.Namespace?.StartsWith("ExileCore.PoEMemory") == true);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        switch (value)
        {
            case null:
            case Delegate:
                writer.WriteNull();
                return;
            case ItemMod mod:
                writer.WriteStartObject();
                writer.WritePropertyName("RawName");
                writer.WriteValue(mod.RawName);
                writer.WritePropertyName("Name");
                writer.WriteValue(mod.Name);
                writer.WritePropertyName("Values");
                writer.WriteStartArray();
                foreach (var v in mod.Values ?? [])
                {
                    writer.WriteValue(v);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            default:
                writer.WriteValue($"<{value.GetType().Name}>");
                return;
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
        throw new NotSupportedException();
}
