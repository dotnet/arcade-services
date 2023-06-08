// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.DotNet.Darc.Tests.Helpers;

// Copied from https://www.codeproject.com/articles/501610/getting-console-output-within-a-unit-test
// Code provided under Code Project Open License 1.02, https://www.codeproject.com/info/cpol10.aspx

public sealed class ConsoleOutputIntercepter : IDisposable
{
    private readonly StringWriter _stringWriter;
    private readonly TextWriter _originalOutput;
    private readonly StringWriter _errorWriter;
    private readonly TextWriter _errorOutput;

    public ConsoleOutputIntercepter()
    {
        _stringWriter = new();
        _originalOutput = Console.Out;
        _errorWriter = new();
        _errorOutput = Console.Error;
        Console.SetOut(_stringWriter);
        Console.SetError(_errorWriter);
    }

    public string GetOuput() => _stringWriter.ToString();

    public string GetError() => _errorWriter.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOutput);
        Console.SetError(_errorOutput);
        _stringWriter.Dispose();
        _errorOutput.Dispose();
    }
}
