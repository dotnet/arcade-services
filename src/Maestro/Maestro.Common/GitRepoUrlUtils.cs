// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Maestro.Common;

public enum GitRepoType
{
    GitHub,
    AzureDevOps,
    Local,
    None
}

public static partial class GitRepoUrlUtils
{
    [GeneratedRegex(@"https://api.github.com/repos/(?<org>[^/]+)/(?<repo>[^/]+)/pulls/(?<id>[0-9]+)/?")]
    private static partial Regex GitHubApiPrUrlRegex();

    [GeneratedRegex(@"https://dev.azure.com/(?<org>[^/]+)/(?<project>[^/]+)/_apis/git/repositories/(?<repo>[^/]+)/pullRequests/(?<id>[0-9]+)/?")]
    private static partial Regex AzdoApiPrUrlRegex();

    private static readonly ConcurrentDictionary<string, string> WellKnownIds = new(
        new Dictionary<string, string>
        {
            ["7ea9116e-9fac-403d-b258-b31fcf1bb293"] = "internal", // https://dev.azure.com/dnceng/internal
            ["0bdbc590-a062-4c3f-b0f6-9383f67865ee"] = "DevDiv", // https://dev.azure.com/devdiv/DevDiv
            ["55e8140e-57ac-4e5f-8f9c-c7c15b51929d"] = "ProjectReunion", // https://dev.azure.com/microsoft/ProjectReunion
        });

    private static string ResolveWellKnownIds(string str)
    {
        foreach (var pair in WellKnownIds)
        {
            str = str.Replace(pair.Key, pair.Value);
        }

        return str;
    }

    /// <summary>
    /// Returns true if the given URL matches the Azure DevOps API pull request URL format.
    /// </summary>
    public static bool IsAzdoApiPrUrl(string url) => AzdoApiPrUrlRegex().IsMatch(url);

    /// <summary>
    /// Converts a GitHub or Azure DevOps API pull request URL to its corresponding web URL.
    /// If the URL does not match a known API format, it is returned unchanged.
    /// </summary>
    public static string TurnApiUrlToWebsite(string url, string? orgName = null, string? repoName = null)
    {
        var match = GitHubApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            return $"https://github.com/{match.Groups["org"]}/{match.Groups["repo"]}/pull/{match.Groups["id"]}";
        }

        match = AzdoApiPrUrlRegex().Match(url);
        if (match.Success)
        {
            // If we have the repo name, use it to replace the repo GUID in the URL
            if (repoName != null)
            {
                WellKnownIds[match.Groups["repo"].Value] = orgName + "-" + repoName;
            }

            var org = ResolveWellKnownIds(match.Groups["org"].Value);
            var project = ResolveWellKnownIds(match.Groups["project"].Value);
            var repo = ResolveWellKnownIds(match.Groups["repo"].Value);
            return $"https://dev.azure.com/{org}/{project}/_git/{repo}/pullrequest/{match.Groups["id"]}";
        }

        return url;
    }

    private const string GitHubComString = "github.com";
    private const string GitHubUrlPrefix = $"https://{GitHubComString}/";
    private const string AzureDevOpsUrlPrefix = "https://dev.azure.com/";

    public static GitRepoType ParseTypeFromUri(string pathOrUri)
    {
        if (!Uri.TryCreate(pathOrUri, UriKind.RelativeOrAbsolute, out Uri? parsedUri))
        {
            return GitRepoType.None;
        }

        // Local relative paths are still a local git URI
        if (!parsedUri.IsAbsoluteUri)
        {
            return pathOrUri.IndexOfAny(Path.GetInvalidPathChars()) == -1
                ? GitRepoType.Local
                : GitRepoType.None;
        }

        return parsedUri switch
        {
            { IsFile: true } => GitRepoType.Local,
            { Scheme: "https" or "http", Host: GitHubComString } => GitRepoType.GitHub,
            { Scheme: "https" or "http", Host: "dev.azure.com" } => GitRepoType.AzureDevOps,
            { Scheme: "https" or "http", Host: var host } when host.EndsWith("visualstudio.com") => GitRepoType.AzureDevOps,
            _ => GitRepoType.None,
        };
    }

    /// <summary>
    /// Sorts so that we go Local -> GitHub -> AzDO.
    /// (in other words local, public, internal)
    /// </summary>
    public static int OrderByLocalPublicOther(GitRepoType first, GitRepoType second)
    {
        if (first == second)
        {
            return 0;
        }

        if (first == GitRepoType.Local)
        {
            return -1;
        }

        if (second == GitRepoType.Local)
        {
            return 1;
        }

        if (first == GitRepoType.GitHub)
        {
            return -1;
        }

        return 1;
    }

    public static IEnumerable<string> OrderRemotesByLocalPublicOther(this IEnumerable<string> uris)
        => uris.OrderBy(ParseTypeFromUri, Comparer<GitRepoType>.Create(OrderByLocalPublicOther));

    public static (string RepoName, string Org) GetRepoNameAndOwner(string uri)
    {
        var repoType = ParseTypeFromUri(uri);

        if (repoType == GitRepoType.AzureDevOps)
        {
            string[] repoParts = uri.Substring(uri.LastIndexOf('/') + 1).Split(['-'], 2);

            if (repoParts.Length != 2)
            {
                throw new ArgumentException($"Repo URI '{uri}' does not end with the expected <GH organization>-<GH repo> format.");
            }

            string org = repoParts[0];
            string repo = repoParts[1];

            // The internal Nuget.Client repo has suffix which needs to be accounted for.
            const string trustedSuffix = "-Trusted";
            if (uri.EndsWith(trustedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(0, repo.Length - trustedSuffix.Length);
            }

            return new(repo, org);
        }

        if (repoType == GitRepoType.GitHub)
        {
            string[] repoParts = uri.Substring(uri.IndexOf(GitHubComString, StringComparison.OrdinalIgnoreCase) + GitHubComString.Length).Split(['/'], StringSplitOptions.RemoveEmptyEntries);

            if (repoParts.Length != 2)
            {
                throw new ArgumentException($"Repo URI '{uri}' does not end with the expected <GH organization>/<GH repo> format.");
            }

            return new(repoParts[1], repoParts[0]);
        }

        // Support owner/repo format (e.g. "dotnet/arcade-services")
        if (repoType == GitRepoType.Local)
        {
            string[] repoParts = uri.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (repoParts.Length == 2)
            {
                return new(repoParts[1], repoParts[0]);
            }
        }

        throw new ArgumentException("Unsupported format of repository url " + uri);
    }

    public static string ConvertInternalUriToPublic(string uri)
    {
        if (!uri.StartsWith(AzureDevOpsUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var (repo, org) = GetRepoNameAndOwner(uri);
        return $"{GitHubUrlPrefix}{org}/{repo}";
    }

    public static string GetRepoAtCommitUri(string repoUri, string commit)
        => ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.AzureDevOps => $"{repoUri}?version=GC{commit}",
            GitRepoType.GitHub => $"{repoUri}/tree/{commit}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
        };

    public static string GetRepoAtBranchUri(string repoUri, string branch)
        => ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.AzureDevOps => $"{repoUri}?version=GB{branch}",
            GitRepoType.GitHub => $"{repoUri}/tree/{branch}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
        };

    public static string GetCommitUri(string repoUri, string commit)
        => ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.AzureDevOps => $"{repoUri}?_a=history&version=GC{commit}",
            GitRepoType.GitHub => $"{repoUri}/commit/{commit}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
        };

    public static string GetRepoFileAtCommitUri(string repoUri, string commit, string filePath)
        => ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.AzureDevOps => $"{repoUri}?version=GC{commit}&path={filePath}",
            GitRepoType.GitHub => $"{repoUri}/blob/{commit}/{filePath}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri)),
        };

    public static string GetVmrFileAtCommitUri(string vmrUri, string productDirectory, string commit, string filePath)
        => ParseTypeFromUri(vmrUri) switch
        {
            GitRepoType.AzureDevOps => $"{vmrUri}?version=GC{commit}&path=src/{productDirectory}/{filePath}",
            GitRepoType.GitHub => $"{vmrUri}/blob/{commit}/src/{productDirectory}/{filePath}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(vmrUri)),
        };

    public static string GetRepoFileAtBranchUri(string repoUri, string branch, string filePath)
        => ParseTypeFromUri(repoUri) switch
        {
            GitRepoType.AzureDevOps => $"{repoUri}?version=GB{branch}&path={filePath}",
            GitRepoType.GitHub => $"{repoUri}/blob/{branch}/{filePath}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(repoUri))
        };

    public static string GetVmrFileAtBranchUri(string vmrUri, string productDirectory, string branch, string filePath)
        => ParseTypeFromUri(vmrUri) switch
        {
            GitRepoType.AzureDevOps => $"{vmrUri}?version=GB{branch}&path=src/{productDirectory}/{filePath}",
            GitRepoType.GitHub => $"{vmrUri}/blob/{branch}/src/{productDirectory}/{filePath}",
            _ => throw new ArgumentException("Unknown git repository type", nameof(vmrUri))
        };

    public static string GetRepoNameWithOrg(string uri)
    {
        try
        {
            var (repo, org) = GetRepoNameAndOwner(uri);
            return $"{org}/{repo}";
        }
        catch (ArgumentException)
        {
            return string.Join("/", uri.Split(['/'], StringSplitOptions.RemoveEmptyEntries).TakeLast(2));
        }
    }
}
