// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
public abstract class VersionFileProperty
{
    public abstract string Name { get; }
    public abstract object? Value { get; }
    public abstract bool IsAdded();
    public abstract bool IsRemoved();
    public abstract bool IsUpdated();
    public abstract bool IsGreater(VersionFileProperty otherProperty);

    public static bool operator >(VersionFileProperty first, VersionFileProperty second)
        => first.IsGreater(second);

    public static bool operator <(VersionFileProperty first, VersionFileProperty second)
        => !first.IsGreater(second);
}
