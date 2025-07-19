// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public interface ISourceManifest
{
    IReadOnlyCollection<IVersionedSourceComponent> Repositories { get; }
    IReadOnlyCollection<ISourceComponent> Submodules { get; }

    string ToJson();
    void RemoveRepository(string repository);
    void RemoveSubmodule(ISourceComponent submodule);
    void UpdateSubmodule(ISourceComponent submodule);
    void UpdateVersion(string repository, string uri, string sha, int? barId);
    VmrDependencyVersion? GetVersion(string repository);
    bool TryGetRepoVersion(string mappingName, [NotNullWhen(true)] out ISourceComponent? mapping);
    ISourceComponent GetRepoVersion(string mappingName);
    RepositoryRecord GetRepositoryRecord(string mapppingName);
    void Refresh(string sourceManifestPath);
}

/// <summary>
/// A model for source-manifest.json file which VMR uses to keep track of
/// synchronized sources
/// </summary>
public class SourceManifest : ISourceManifest
{
    private SortedSet<RepositoryRecord> _repositories;
    private SortedSet<SubmoduleRecord> _submodules;

    public IReadOnlyCollection<IVersionedSourceComponent> Repositories => _repositories;

    public IReadOnlyCollection<ISourceComponent> Submodules => _submodules;

    public SourceManifest(IEnumerable<RepositoryRecord> repositories, IEnumerable<SubmoduleRecord> submodules)
    {
        _repositories = [.. repositories];
        _submodules = [.. submodules];
    }

    public void UpdateVersion(string repository, string uri, string sha, int? barId)
    {
        var repo = _repositories.FirstOrDefault(r => r.Path == repository);
        if (repo != null)
        {
            repo.CommitSha = sha;
            repo.RemoteUri = uri;

            if (barId != null)
            {
                repo.BarId = barId;
            }
        }
        else
        {
            _repositories.Add(new RepositoryRecord(repository, uri, sha, barId));
        }
    }

    public void RemoveRepository(string repository)
    {
        var repo = _repositories.FirstOrDefault(r => r.Path == repository);
        if (repo != null)
        {
            _repositories.Remove(repo);
        }

        _submodules.RemoveWhere(s => s.Path.StartsWith(repository + "/"));
    }

    public void RemoveSubmodule(ISourceComponent submodule)
    {
        var repo = _submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if (repo != null)
        {
            _submodules.Remove(repo);
        }
    }

    public void UpdateSubmodule(ISourceComponent submodule)
    {
        var repo = _submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if (repo != null)
        {
            repo.CommitSha = submodule.CommitSha;
            repo.RemoteUri = submodule.RemoteUri;
        }
        else
        {
            _submodules.Add(new SubmoduleRecord(submodule.Path, submodule.RemoteUri, submodule.CommitSha));
        }
    }

    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        // Wrap SourceManifest for serialization
        var sourceManifestWrapper = ToWrapper();

        return JsonSerializer.Serialize(sourceManifestWrapper, options);
    }

    public void Refresh(string sourceManifestPath)
    {
        var newManifst = FromFile(sourceManifestPath);
        _repositories = newManifst._repositories;
        _submodules = newManifst._submodules;
    }

    public bool TryGetRepoVersion(string mappingName, [NotNullWhen(true)] out ISourceComponent? version)
    {
        version = Repositories.FirstOrDefault(m => m.Path.Equals(mappingName, StringComparison.InvariantCultureIgnoreCase));
        version ??= Submodules.FirstOrDefault(m => m.Path.Equals(mappingName, StringComparison.InvariantCultureIgnoreCase));
        return version != null;
    }

    public ISourceComponent GetRepoVersion(string mappingName)
        => TryGetRepoVersion(mappingName, out var version)
            ? version
            : throw new Exception($"No manifest record named {mappingName} found");

    public static SourceManifest FromFile(string path)
    {
        return !File.Exists(path)
            ? new SourceManifest([], [])
            : FromJson(File.ReadAllText(path));
    }

    public static SourceManifest FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var wrapper = JsonSerializer.Deserialize<SourceManifestWrapper>(json, options)
            ?? throw new Exception("Failed to deserialize source-manifest.json");

        return wrapper.ToSourceManifest();
    }
    internal SourceManifestWrapper ToWrapper()
    {
        return new SourceManifestWrapper
        {
            Repositories = _repositories,
            Submodules = _submodules,
        };
    }

    public VmrDependencyVersion? GetVersion(string repository)
    {
        var repositoryRecord = _repositories.FirstOrDefault(r => r.Path == repository);
        if (repositoryRecord != null)
        {
            return new(repositoryRecord.CommitSha);
        }
        else
        {
            return null;
        }
    }

    public RepositoryRecord GetRepositoryRecord(string mapppingName)
    {
        return _repositories.FirstOrDefault(r => r.Path == mapppingName)
            ?? throw new Exception($"No repository record named {mapppingName} found");
    }
}

/// <summary>
/// We use this for JSON deserialization because we're on .NET 6.0 and the ctor deserialization doesn't work.
/// </summary>
internal class SourceManifestWrapper
{
    public ICollection<RepositoryRecord> Repositories { get; init; } = [];
    public ICollection<SubmoduleRecord> Submodules { get; init; } = [];
    internal SourceManifest ToSourceManifest()
    {
        return new SourceManifest(Repositories, Submodules);
    }
}
