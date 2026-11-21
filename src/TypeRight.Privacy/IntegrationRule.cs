using System;
using Newtonsoft.Json;

namespace TypeRight.Privacy
{
    /// <summary>
    /// A local integration/sensitivity rule. The field shape mirrors the
    /// observed client rule format so the shipped rule file can be consumed
    /// without translation: ElementName, IsEnabled, Language,
    /// IsSensitivityCheck, Notes.
    /// </summary>
    public sealed class IntegrationRule
    {
        [JsonProperty("ElementName")]
        public string ElementName { get; set; }

        [JsonProperty("IsEnabled")]
        [JsonConverter(typeof(FlexibleBoolConverter))]
        public bool IsEnabled { get; set; }

        [JsonProperty("Language")]
        public string Language { get; set; }

        [JsonProperty("IsSensitivityCheck")]
        [JsonConverter(typeof(FlexibleBoolConverter))]
        public bool IsSensitivityCheck { get; set; }

        [JsonProperty("Notes")]
        public string Notes { get; set; }
    }

    /// <summary>
    /// Some shipped rules carry an empty string instead of a boolean. Treat
    /// anything that is not explicitly "true" as disabled (fail-closed).
    /// </summary>
    public sealed class FlexibleBoolConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(bool);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Boolean)
                return (bool)reader.Value;
            if (reader.TokenType == JsonToken.String)
            {
                bool value;
                return bool.TryParse((string)reader.Value, out value) && value;
            }
            return false;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue((bool)value);
        }
    }
}
