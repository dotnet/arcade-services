// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.Darc.Helpers;
internal class Disposable : IDisposable
{
    private Action _dispose;

    public static IDisposable Create(Action dispose)
    {
        return new Disposable(dispose);
    }

    private Disposable(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose()
    {
        _dispose?.Invoke();
    }
}

internal class DisposableValue<T> : IDisposable
{
    private Action _dispose;
    public T Value { get; private set; }

    internal DisposableValue(Action dispose, T value)
    {
        _dispose = dispose;
        Value = value;
    }

    public void Dispose()
    {
        _dispose.Invoke();
    }
}
