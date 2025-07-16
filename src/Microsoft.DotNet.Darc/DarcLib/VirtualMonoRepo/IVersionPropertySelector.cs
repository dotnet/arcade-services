// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;
public interface IVersionPropertySelector<T> where T : IVersionFileProperty
{
    T Select(T first, T second);
}
