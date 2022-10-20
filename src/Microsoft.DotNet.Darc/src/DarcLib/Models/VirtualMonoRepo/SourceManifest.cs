// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

/// <summary>
/// A model for source-manifest.json file which VMR uses to keep track of
/// synchronized sources
/// </summary>
public class SourceManifest
{
    public ICollection<RepositoryRecord> Repositories { get; set; }
    public ICollection<SubmoduleRecord> Submodules { get; set; }

    public SourceManifest()
    {
        Repositories = new SortedSet<RepositoryRecord>();
        Submodules = new SortedSet<SubmoduleRecord>();
    }

    public void UpdateVersion(string repository, string uri, string sha, string packageVersion)
    {
        var repo = Repositories.FirstOrDefault(r => r.Path == repository);
        if (repo != null)
        {
            repo.CommitSha = sha;
            repo.RemoteUri = uri;
            repo.PackageVersion = packageVersion;
        }
        else
        {
            Repositories.Add(new RepositoryRecord(repository, sha, uri, packageVersion));
        }
    }

    public void RemoveSubmodule(SubmoduleRecord submodule)
    {
        var repo = Submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if(repo != null)
        {
            Submodules.Remove(repo);
        }
    }

    public void UpdateSubmodule(SubmoduleRecord submodule)
    {
        var repo = Submodules.FirstOrDefault(r => r.Path == submodule.Path);
        if (repo != null)
        {
            repo.CommitSha = submodule.CommitSha;
            repo.RemoteUri = submodule.RemoteUri;
        }
        else
        {
            Submodules.Add(submodule);
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

        return JsonSerializer.Serialize<SourceManifest>(this, options);
    }

    public static SourceManifest FromJson(Stream stream)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        return JsonSerializer.Deserialize<SourceManifest>(stream, options) 
            ?? throw new Exception($"Failed to deserialize source manifest");
    }

    public static SourceManifest FromJson(string path)
    {
        if (!File.Exists(path))
        {
            return new SourceManifest();
        }

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        return JsonSerializer.Deserialize<SourceManifest>(stream, options)
            ?? throw new Exception($"Failed to deserialize {path}");
    }
}
