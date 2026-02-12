// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildInsights.BuildAnalysis.Models;

public class TestOutcomeValueConverter : JsonConverter<TestOutcomeValue>
{
    public override TestOutcomeValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return TestOutcomeValue.Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, TestOutcomeValue value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

[JsonConverter(typeof(TestOutcomeValueConverter))]
public class TestOutcomeValue : IEquatable<TestOutcomeValue>, IEquatable<string>
{
    private TestOutcomeValue(string value)
    {
        Value = value;
    }

    public string Value { get; }
    
    public static readonly TestOutcomeValue Passed = new TestOutcomeValue("Passed");
    public static readonly TestOutcomeValue Failed = new TestOutcomeValue("Failed");
    public static readonly TestOutcomeValue PassedOnRerun = new TestOutcomeValue("PassedOnRerun");

    public static TestOutcomeValue Parse(string value)
    {
        return new TestOutcomeValue(value);
    }

    public bool Equals(TestOutcomeValue other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(string other)
    {
        return string.Equals(Value, other, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is string str)
        {
            return Equals(str);
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((TestOutcomeValue) obj);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
    }

    public static bool operator ==(TestOutcomeValue left, TestOutcomeValue right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(TestOutcomeValue left, TestOutcomeValue right)
    {
        return !Equals(left, right);
    }

    public static bool operator ==(TestOutcomeValue left, string right)
    {
        if (left is null)
            return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(TestOutcomeValue left, string right)
    {
        if (left is null)
            return right is null;
        return !left.Equals(right);
    }

    public static bool operator ==(string left, TestOutcomeValue right)
    {
        if (right is null)
            return left is null;
        return right.Equals(left);
    }

    public static bool operator !=(string left, TestOutcomeValue right)
    {
        if (right is null)
            return left is null;
        return !right.Equals(left);
    }
}
