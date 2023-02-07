// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace Microsoft.DotNet.Darc.Tests.Helpers;

// Copied from https://www.codeproject.com/articles/501610/getting-console-output-within-a-unit-test
// Code provided under Code Project Open License 1.02, https://www.codeproject.com/info/cpol10.aspx

public sealed class ConsoleOutputIntercepter : IDisposable
{
    private readonly StringWriter _stringWriter;
    private readonly TextWriter _originalOutput;

    public ConsoleOutputIntercepter()
    {
        _stringWriter = new();
        _originalOutput = Console.Out;
        Console.SetOut(_stringWriter);
    }

    public string GetOuput() => _stringWriter.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOutput);
        _stringWriter.Dispose();
    }
}
