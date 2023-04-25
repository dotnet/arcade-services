// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.DotNet.Kusto.Tests;

public static class MockOptionMonitor
{
    public static MockOptionMonitor<TOptions> Create<TOptions>(TOptions opts) where TOptions : class, new() => new MockOptionMonitor<TOptions>(opts);
}

public class MockOptionMonitor<TOptions> : IOptionsMonitor<TOptions>, IOptionsSnapshot<TOptions>, IOptions<TOptions>
    where TOptions : class, new()
{
    public MockOptionMonitor(TOptions value)
    {
        Value = value;
    }

    TOptions IOptionsMonitor<TOptions>.Get(string name) => Value;

    public IDisposable OnChange(Action<TOptions, string> listener)
    {
        return Mock.Of<IDisposable>();
    }

    public TOptions CurrentValue => Value;
    public TOptions Value { get; }
    TOptions IOptionsSnapshot<TOptions>.Get(string name) => Value;
}
