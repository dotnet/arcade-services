// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

/// <summary>
/// Represents a record in the source-manifest.json file which VMR uses to keep track of
/// synchronized sources
/// </summary>
public abstract class ManifestRecord : IComparable<ManifestRecord>
{
    public string Path { get; set; }
    public string RemoteUri { get; set; }
    public string CommitSha { get; set; }

    public ManifestRecord(string path, string remoteUri, string commitSha)
    {
        Path = path;
        RemoteUri = remoteUri;
        CommitSha = commitSha;
    }

    public int CompareTo(ManifestRecord? other)
    {
        if (other == null)
        {
            return 1;
        }

        return Path.CompareTo(other.Path);
    }
}

public class RepositoryRecord : ManifestRecord
{
    public RepositoryRecord(string path, string remoteUri, string commitSha, string packageVersion) 
        : base(path, remoteUri, commitSha)
    {
        PackageVersion = packageVersion;
    }

    public string PackageVersion { get; set; } = null!;
}

public class SubmoduleRecord : ManifestRecord
{
    public SubmoduleRecord(string path, string remoteUri, string commitSha) 
        : base(path, remoteUri, commitSha)
    {
    }
}

// Read-only version that will be visible to outside classes
public record SubmoduleInfo(string Path, string RemoteUri, string CommitSha)
{
    public static SubmoduleInfo FromRecord(SubmoduleRecord other) => new(other.Path, other.RemoteUri, other.CommitSha);
}
