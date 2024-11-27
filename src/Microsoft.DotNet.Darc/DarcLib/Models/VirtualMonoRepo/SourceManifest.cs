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
    IReadOnlyCollection<ISourceComponent> Repositories { get; }
    IReadOnlyCollection<ISourceComponent> Submodules { get; }

    string ToJson();
    void RemoveRepository(string repository);
    void RemoveSubmodule(SubmoduleRecord submodule);
    void UpdateSubmodule(SubmoduleRecord submodule);
    void UpdateVersion(string repository, string uri, string sha);
    string? GetVersion(string repository);
    bool TryGetRepoVersion(string mappingName, [NotNullWhen(true)] out ISourceComponent? mapping);
    ISourceComponent GetRepoVersion(string mappingName);
    void Refresh(string sourceManifestPath);
}

/// <summary>
/// A model for source-manifest.json file which VMR uses to keep track of
/// synchronized sources
/// </summary>
public class SourceManifest : ISourceManifest
{
    private SortedSet<ManifestRecord> _repositories;
    private SortedSet<SubmoduleRecord> _submodules;

    public IReadOnlyCollection<ISourceComponent> Repositories => _repositories;

    public IReadOnlyCollection<ISourceComponent> Submodules => _submodules;

    public SourceManifest(IEnumerable<ManifestRecord> repositories, IEnumerable<SubmoduleRecord> submodules)
    {
        _repositories = [.. repositories];
        _submodules = [.. submodules];
    }

    public void UpdateVersion(string repository, string uri, string sha)
    {
        var repo = _repositories.FirstOrDefault(r => r.Path == repository);
        if (repo != null)
        {
            repo.CommitSha = sha;
            repo.RemoteUri = uri;
        }
        else
        {
            _repositories.Add(new ManifestRecord(repository, uri, sha));
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

    public void RemoveSubmodule(SubmoduleRecord submodule)
    {
        var repo = _submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if (repo != null)
        {
            _submodules.Remove(repo);
        }
    }

    public void UpdateSubmodule(SubmoduleRecord submodule)
    {
        var repo = _submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if (repo != null)
        {
            repo.CommitSha = submodule.CommitSha;
            repo.RemoteUri = submodule.RemoteUri;
        }
        else
        {
            _submodules.Add(submodule);
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

        var data = new SourceManifestWrapper
        {
            Repositories = _repositories,
            Submodules = _submodules,
        };

        return JsonSerializer.Serialize(data, options);
    }

    public void Refresh(string sourceManifestPath)
    {
        var newManifst = FromJson(sourceManifestPath);
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

    public static SourceManifest FromJson(string path)
    {
        if (!File.Exists(path))
        {
            return new SourceManifest([], []);
        }

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        var wrapper = JsonSerializer.Deserialize<SourceManifestWrapper>(stream, options)
            ?? throw new Exception($"Failed to deserialize {path}");

        return new SourceManifest(wrapper.Repositories, wrapper.Submodules);
    }

    public string? GetVersion(string repository)
    {
        var repositoryRecord = _repositories.FirstOrDefault(r => r.Path == repository);
        if (repositoryRecord != null)
        {
            return repositoryRecord.CommitSha;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// We use this for JSON deserialization because we're on .NET 6.0 and the ctor deserialization doesn't work.
    /// </summary>
    private class SourceManifestWrapper
    {
        public ICollection<ManifestRecord> Repositories { get; init; } = [];
        public ICollection<SubmoduleRecord> Submodules { get; init; } = [];
    }
}
