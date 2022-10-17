using Microsoft.DotNet.DarcLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo
{
    public class SourceManifest
    {
        public ICollection<RepositoryRecord> Repositories { get; set; }
        public ICollection<SubmoduleRecord> Submodules { get; set; }

        public SourceManifest()
        {
            Repositories = new List<RepositoryRecord>();
            Submodules = new List<SubmoduleRecord>();
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
                Repositories.Add(new RepositoryRecord 
                { 
                    Path = repository,
                    CommitSha = sha,
                    RemoteUri = uri,
                    PackageVersion = packageVersion
                });
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

            return JsonSerializer.Deserialize<SourceManifest>(stream, options);
        }
    }

    public class RepositoryRecord
    {
        public string Path { get; set; }
        public string RemoteUri { get; set; }
        public string CommitSha { get; set; }
        public string PackageVersion { get; set; }
    }

    public class SubmoduleRecord
    {
        string Path { get; set; }
        string RemoteUri { get; set; }
        string CommitSha { get; set; }
        string PackageVersion { get; set; }
    };
}
