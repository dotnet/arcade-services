// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
namespace Maestro.Common;

public enum GitRepoType
{
    GitHub,
    AzureDevOps,
    Local,
    None
}

public static class GitRepoUrlUtils
{
    private const string GitHubComString = "github.com";
    private const string GitHubUrlPrefix = "https://github.com/";
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
            { Scheme: "https" or "http", Host: "github.com" } => GitRepoType.GitHub,
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
            string[] repoParts = uri.Substring(uri.IndexOf(GitHubComString) + GitHubComString.Length).Split(['/'], StringSplitOptions.RemoveEmptyEntries);

            if (repoParts.Length != 2)
            {
                throw new ArgumentException($"Repo URI '{uri}' does not end with the expected <GH organization>/<GH repo> format.");
            }

            return new(repoParts[1], repoParts[0]);
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
