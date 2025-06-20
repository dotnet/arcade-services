// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public interface ISourceComponent
{
    /// <summary>
    /// Path where the component is located in the VMR.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// URI from which the component has been synchronized from.
    /// </summary>
    public string RemoteUri { get; }

    /// <summary>
    /// Original commit SHA from which the component has been synchronized.
    /// </summary>
    public string CommitSha { get; }

    public string GetPublicUrl()
    {
        var url = RemoteUri;
        const string GitSuffix = ".git";

        switch (GitRepoUrlUtils.ParseTypeFromUri(url))
        {
            case GitRepoType.GitHub:
                if (url.EndsWith(GitSuffix))
                {
                    url = url[..^GitSuffix.Length];
                }

                if (!url.EndsWith('/'))
                {
                    url += '/';
                }

                url += "tree/" + CommitSha;

                return url;
            case GitRepoType.AzureDevOps:
                return url + "/?version=GC" + CommitSha;
            default:
                return url;
        }
    }
}

public interface IVersionedSourceComponent : ISourceComponent
{
    public int? BarId { get; }
}

/// <summary>
/// Represents a record in the source-manifest.json file which VMR uses to keep track of
/// synchronized sources
/// </summary>
public abstract class ManifestRecord : IComparable<ISourceComponent>, ISourceComponent
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

    public int CompareTo(ISourceComponent? other)
    {
        if (other == null)
        {
            return 1;
        }

        return Path.CompareTo(other.Path);
    }
}

public class RepositoryRecord : ManifestRecord, IVersionedSourceComponent
{
    public RepositoryRecord(
        string path,
        string remoteUri,
        string commitSha,
        int? barId)
        : base(path, remoteUri, commitSha)
    {
        BarId = barId;
    }

    public int? BarId { get; set; }
}

public class SubmoduleRecord : ManifestRecord
{
    public SubmoduleRecord(string path, string remoteUri, string commitSha)
        : base(path, remoteUri, commitSha)
    {
    }
}
