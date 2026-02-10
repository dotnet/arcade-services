// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildInsights.KnownIssues.Models;

public class ErrorOrArrayOfErrorsConverter : JsonConverter<List<string>>
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(string) || objectType == typeof(List<string>);
    }

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string value = reader.GetString();
            return string.IsNullOrEmpty(value) ? new List<string>() : new List<string> {value};
        }

        var elements = new List<string>();
        reader.Read();
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            string value = reader.GetString();

            if (!string.IsNullOrEmpty(value))
            {
                elements.Add(value);
            }

            reader.Read();
        }

        return elements;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(options);
        newOptions.Converters.Remove(this);

        JsonSerializer.Serialize(writer, value, newOptions);
    }
}
