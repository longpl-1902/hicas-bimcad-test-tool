using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace HicasTest.Runner.Model;

/// <summary>
/// Reads a YAML value as JSON with the types a person means: a plain 100 is a number, true a boolean, null null;
/// a quoted "100" stays a string. (YamlDotNet's untyped reading would turn every scalar into a string and
/// the feature's request would not deserialize.)
/// </summary>
public sealed class JsonNodeYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(JsonNode);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) => Read(parser);

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        emitter.Emit(new Scalar(((JsonNode?)value)?.ToJsonString() ?? "null"));

    private static JsonNode? Read(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
            return FromScalar(scalar);

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var array = new JsonArray();
            while (!parser.TryConsume<SequenceEnd>(out _))
                array.Add(Read(parser));
            return array;
        }

        if (parser.TryConsume<MappingStart>(out _))
        {
            var obj = new JsonObject();
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                obj[key] = Read(parser);
            }
            return obj;
        }

        throw new YamlException("argument supports mappings, lists and scalars only (no anchors or aliases).");
    }

    internal static JsonNode? FromScalar(Scalar scalar)
    {
        if (scalar.Style != ScalarStyle.Plain)
            return JsonValue.Create(scalar.Value);

        var text = scalar.Value;
        if (text.Length == 0 || text is "null" or "~" or "Null" or "NULL")
            return null;
        if (text is "true" or "True" or "TRUE")
            return JsonValue.Create(true);
        if (text is "false" or "False" or "FALSE")
            return JsonValue.Create(false);
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
            return JsonValue.Create(whole);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            return JsonValue.Create(number);
        return JsonValue.Create(text);
    }
}
