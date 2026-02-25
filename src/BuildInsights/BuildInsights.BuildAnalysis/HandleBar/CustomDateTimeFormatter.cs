// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.IO;

namespace BuildInsights.BuildAnalysis.HandleBar;

public sealed class CustomDateTimeFormatter : IFormatter, IFormatterProvider
{
    private readonly string _format;

    public CustomDateTimeFormatter(string format) => _format = format;

    public void Format<T>(T value, in EncodedTextWriter writer)
    {
        if (value is not DateTimeOffset dateTime)
            throw new ArgumentException("supposed to be DateTimeOffset");

        writer.WriteSafeString(dateTime.ToString(_format));
    }

    public bool TryCreateFormatter(Type type, out IFormatter? formatter)
    {
        if (type != typeof(DateTimeOffset))
        {
            formatter = null;
            return false;
        }

        formatter = this;
        return true;
    }
}
