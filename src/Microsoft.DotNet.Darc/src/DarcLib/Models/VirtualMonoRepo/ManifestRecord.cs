// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

public abstract class ManifestRecord : IComparable<ManifestRecord>
{
    public string Path { get; set; } = null!;
    public string RemoteUri { get; set; } = null!;
    public string CommitSha { get; set; } = null!;

    public int CompareTo(ManifestRecord? other)
    {
        if (other == null) return 1;
        return Path.CompareTo(other.Path);
    }
}

public class RepositoryRecord : ManifestRecord
{
    public string PackageVersion { get; set; } = null!;
}

public class SubmoduleRecord : ManifestRecord { }
