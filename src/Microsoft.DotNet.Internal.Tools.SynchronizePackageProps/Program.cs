using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Mono.Options;
using Octokit;

namespace Microsoft.DotNet.Internal.Tools.SynchronizePackageProps
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            return (int) await Process(args);
        }

        private static async Task<Errors> Process(string[] args)
        {
            Errors errors = 0;
            string dir = null;
            bool fix = false;

            var options = new OptionSet
            {
                {
                    "directory|dir|d=", "Directory of eng folder containing Packages.props and Version.Details.xml",
                    v => dir = v
                },
                {"fix", "Fix mismatched errors to found when possible, rather than reporting", v => fix = v != null}
            };

            List<string> unparsed = options.Parse(args);

            if (unparsed.Count > 0 && dir == null)
            {
                dir = unparsed[0];
                unparsed.RemoveAt(0);
            }

            if (unparsed.Count > 0)
            {
                WriteError($"Unexpected argument '{unparsed[0]}'");
                return Errors.BadArguments;
            }

            if (args.Length == 1)
            {
                dir = Path.GetFullPath(args[0]);
            }

            string engDir = dir;
            string packageFile = Path.Combine(engDir, "Packages.props");
            if (!File.Exists(packageFile))
            {
                engDir = Path.Combine(dir, "eng");
                packageFile = Path.Combine(engDir, "Packages.props");
                if (!File.Exists(packageFile))
                {
                    WriteError(
                        $"Could not find {packageFile}, pass root or eng directory as first parameter, or run from root");
                    return Errors.MissingFile;
                }
            }

            XDocument localPackageDocument = XDocument.Load(packageFile);

            string versionFile = Path.Combine(engDir, "Version.Details.xml");
            if (!File.Exists(versionFile))
            {
                WriteError(
                    $"Could not find {versionFile}, pass root or eng directory as first parameter, or run from root");
                return Errors.MissingFile;
            }

            XDocument localVersionsDocument = XDocument.Load(versionFile);

            (Dictionary<string, Dependency> remoteDependencies, Errors localErrors) =
                BadLocalFormat(localVersionsDocument);
            errors |= localErrors;

            Dictionary<string, string> localPackages = ParsePackageVersions(localPackageDocument, "local");

            (Dictionary<string, string> collapsedRemotePackages, Errors remoteError) =
                await FetchConsolidatedRemotePackages(remoteDependencies);

            if (remoteError != 0)
            {
                return errors | remoteError;
            }

            foreach ((string remotePackage, string remoteVersion) in collapsedRemotePackages)
            {
                if (!localPackages.TryGetValue(remotePackage, out string localVersion))
                {
                    continue;
                }

                if (remoteVersion == localVersion)
                {
                    continue;
                }

                if (fix)
                {
                    Console.WriteLine(
                        $"Fixing local package '{remotePackage}' to '{remoteVersion}' from '{localVersion}'");
                    localPackages[remotePackage] = remoteVersion;
                }
                else
                {
                    WriteError(
                        $"Package {remotePackage} is mismatched. Local version is '{localVersion}', remote version is '{remoteVersion}'");
                    errors |= Errors.IncoherentPackageVersions;
                }
            }

            if (errors != 0)
            {
                return errors;
            }

            if (!fix)
            {
                Console.WriteLine("No errors detected, Packages.props is coherent");
                return 0;
            }

            foreach (XElement packageRef in localPackageDocument.Descendants("PackageReference"))
            {
                string name = packageRef.Attribute("Update")?.Value;

                if (localPackages.TryGetValue(name, out string newVersion))
                {
                    packageRef.SetAttributeValue("Version", newVersion);
                }
            }

            await using FileStream outputVersionFile = File.Create(packageFile);
            await localPackageDocument.SaveAsync(outputVersionFile, SaveOptions.None, CancellationToken.None);

            return 0;
        }

        private static (Dictionary<string, Dependency> remoteDependencies, Errors errors) BadLocalFormat(
            XDocument localVersionsDocument)
        {
            var remoteDependencies = new Dictionary<string, Dependency>();
            Errors errors = 0;
            foreach (XElement dependency in localVersionsDocument.Descendants("Dependency"))
            {
                string repo = dependency.Element("Uri")?.Value;
                string commitHash = dependency.Element("Sha")?.Value;
                string name = dependency.Attribute("Name")?.Value;

                if (string.IsNullOrEmpty(name))
                {
                    WriteError("<Dependency> element has no name");
                    {
                        return (remoteDependencies, 0);
                    }
                }

                if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(commitHash))
                {
                    WriteWarning($"Dependency {name} is missing repository information, skipping");
                    continue;
                }

                if (!Regex.IsMatch(repo, "^https://github.com/([^/]*)/(.*?)/?$"))
                {
                    WriteWarning($"Repository {repo} for dependency {name} is not github, so will not be synchronized");
                    continue;
                }

                if (remoteDependencies.TryGetValue(repo, out Dependency existingDependency))
                {
                    if (existingDependency.CommitHash != commitHash)
                    {
                        WriteError(
                            $"Incoherent dependency for repository {repo} detected, dependencies {dependency.Name} and {name} do not have the same commit hash");
                        errors |= Errors.IncoherentDependencies;
                    }
                }
                else
                {
                    remoteDependencies.Add(repo, new Dependency(repo, commitHash, name));
                }
            }

            return (remoteDependencies, errors);
        }

        private static async Task<(Dictionary<string, string> value, Errors error)> FetchConsolidatedRemotePackages(
            Dictionary<string, Dependency> remoteDependencies)
        {
            Errors errors = 0;
            var collapsedRemotePackages = new Dictionary<string, string>();
            Dictionary<string, Dictionary<string, string>>
                remotePackages = await FetchRemotePackageVersions(remoteDependencies);

            foreach ((string repo, Dictionary<string, string> versions) in remotePackages)
            {
                foreach ((string package, string version) in versions)
                {
                    foreach ((string otherRepo, Dictionary<string, string> otherVersions) in remotePackages)
                    {
                        if (otherVersions.TryGetValue(package, out string otherVersion) && otherVersion != version)
                        {
                            WriteError(
                                $"Remote package inconsistency for '{package}'. Version '{version}' in '{repo}' and '{otherVersion}' in '{otherRepo}', no consistency possible. update-dependencies to get remote versions in agreement");
                            errors |= Errors.RemotePackagesInconsistent;
                            continue;
                        }

                        collapsedRemotePackages[package] = version;
                    }
                }
            }

            return (collapsedRemotePackages, errors);
        }

        private static async Task<Dictionary<string, Dictionary<string, string>>> FetchRemotePackageVersions(
            Dictionary<string, Dependency> remoteDependencies)
        {
            var assembly = Assembly.GetEntryAssembly();
            var client = new GitHubClient(new ProductHeaderValue(
                "Microsoft.DotNet.Internal.Tools.SynchronizePackageProps",
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                assembly.GetName().Version.ToString()));

            var
                remotePackages = new Dictionary<string, Dictionary<string, string>>();
            foreach ((string repo, Dependency dep) in remoteDependencies)
            {
                Match repoMatch = Regex.Match(repo, "^https://github.com/([^/]*)/(.*?)/?$");
                IReadOnlyList<RepositoryContent> contents;
                string owner = repoMatch.Groups[1].Value;
                string repository = repoMatch.Groups[2].Value;
                try
                {
                    contents = await client.Repository.Content.GetAllContentsByRef(owner,
                        repository,
                        "eng/Packages.props",
                        dep.CommitHash);
                }
                catch (NotFoundException)
                {
                    try
                    {
                        contents = await client.Repository.Content.GetAllContentsByRef(owner,
                            repository,
                            "Packages.props",
                            dep.CommitHash);
                    }
                    catch (NotFoundException)
                    {
                        WriteWarning($"No Packages.props found for '{repo}' at '{dep.CommitHash}'");
                        continue;
                    }
                }

                Dictionary<string, string> result;
                if (string.IsNullOrEmpty(contents[0].Content))
                {
                    if (contents[0].Size > 100000)
                    {
                        WriteWarning($"Packages.props too large ({contents[0].Size}) in {repo}, skipping...");
                        continue;
                    }

                    result = ParsePackageVersions(XDocument.Load(contents[0].DownloadUrl), $"{owner}/{repository}");
                }
                else
                {
                    result = ParsePackageVersions(XDocument.Parse(contents[0].Content), $"{owner}/{repository}");
                }

                remotePackages.Add(repo, result);
            }

            return remotePackages;
        }

        private static Dictionary<string, string> ParsePackageVersions(XDocument packagesDocument, string repoName)
        {
            var props = new Dictionary<string, string>();
            IEnumerable<XElement> propGroups = packagesDocument?.Element("Project")?.Elements("PropertyGroup");
            if (propGroups != null)
            {
                foreach (XElement propGroup in propGroups)
                {
                    if (propGroup.Attribute("Condition") != null)
                    {
                        WriteWarning($"{repoName} Packages.prop has Conditional PropertyGroup, which is not supported");
                        continue;
                    }

                    foreach (XElement prop in propGroup.Elements())
                    {
                        if (prop.Attribute("Condition") != null)
                        {
                            WriteWarning(
                                $"{repoName} Packages.prop property {prop.Name.LocalName} has Conditional PropertyGroup, which is not supported");
                            continue;
                        }

                        props[prop.Name.LocalName] = prop.Value;
                    }
                }
            }

            var versions = new Dictionary<string, string>();
            IEnumerable<XElement> packageReferences = packagesDocument?.Element("Project")
                ?.Elements("ItemGroup")
                ?.Elements("PackageReference");
            if (packageReferences == null)
            {
                WriteError($"No PackageReferences found in {repoName} Packages.props");
            }
            else
            {
                foreach (XElement packageRef in packageReferences)
                {
                    string name = packageRef.Attribute("Update")?.Value;
                    string version = packageRef.Attribute("Version")?.Value;

                    if (string.IsNullOrEmpty(name))
                    {
                        WriteWarning($"PackageReference with no 'Update' found in Packages.props for repo {repoName}");
                        continue;
                    }

                    if (string.IsNullOrEmpty(version))
                    {
                        WriteWarning(
                            $"PackageReference with no 'Version' for Package '{name}' found in Packages.props for repo {repoName}");
                        continue;
                    }

                    version = Regex.Replace(version,
                        @"\$\(([A-Za-z0-9_]*)\)",
                        m => props.GetValueOrDefault(m.Groups[1].Value, m.Groups[0].Value));

                    versions.Add(name, version);
                }
            }

            return versions;
        }

        private static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }

        private static void WriteWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }

        [Flags]
        private enum Errors
        {
            BadArguments = 1,
            MissingFile = 2,
            BadLocalFormat = 3,


            IncoherentDependencies = 1 << 8,
            IncoherentPackageVersions = 1 << 9,
            RemotePackagesInconsistent = 1 << 10
        }

        private struct Dependency
        {
            public string RepositoryUrl { get; }
            public string CommitHash { get; }
            public string Name { get; }

            public Dependency(string repositoryUrl, string commitHash, string name)
            {
                RepositoryUrl = repositoryUrl;
                CommitHash = commitHash;
                Name = name;
            }
        }
    }
}
