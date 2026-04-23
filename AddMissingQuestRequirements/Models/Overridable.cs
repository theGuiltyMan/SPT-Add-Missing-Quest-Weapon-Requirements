using System.Text.Json;
using System.Text.Json.Serialization;

namespace AddMissingQuestRequirements.Models;

/// <summary>
/// Wraps a value that can appear in JSON as either a bare value or
/// { "value": T, "behaviour": "DELETE" } form (used in OverrideBehaviour arrays).
/// </summary>
[JsonConverter(typeof(OverridableConverterFactory))]
public sealed class Overridable<T>
{
    public T Value { get; }
    public OverrideBehaviour? Behaviour { get; }

    public Overridable(T value, OverrideBehaviour? behaviour = null)
    {
        Value = value;
        Behaviour = behaviour;
    }
}

public sealed class OverridableConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Overridable<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OverridableConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class OverridableConverter<T> : JsonConverter<Overridable<T>>
{
    public override Overridable<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // { "value": ..., "behaviour": "DELETE" } form
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var value = root.GetProperty("value").Deserialize<T>(options)!;
            OverrideBehaviour? behaviour = null;
            if (root.TryGetProperty("behaviour", out var behaviourEl))
                behaviour = behaviourEl.Deserialize<OverrideBehaviour>(options);
            return new Overridable<T>(value, behaviour);
        }

        // Bare value form
        var bareValue = JsonSerializer.Deserialize<T>(ref reader, options)!;
        return new Overridable<T>(bareValue);
    }

    public override void Write(Utf8JsonWriter writer, Overridable<T> value, JsonSerializerOptions options)
    {
        if (value.Behaviour is null)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WritePropertyName("behaviour");
        JsonSerializer.Serialize(writer, value.Behaviour.Value, options);
        writer.WriteEndObject();
    }
}
