// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeQLConfigGenerator
{
    Task UpdateCodeQLConfig(CancellationToken cancellationToken);
}

public class CodeQLConfigGenerator : ICodeQLConfigGenerator
{
    private const string GeneratedFileHeader =
        "# This file is auto-generated from .CodeQL.yml and CodeQL.yml files in src/.\n" +
        "# Manual changes will be overwritten.\n";

    private static readonly IReadOnlyList<string> s_codeQLConfigFileNames =
    [
        ".CodeQL.yml",
        "CodeQL.yml",
    ];

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitClient _localGitClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CodeQLConfigGenerator> _logger;

    public CodeQLConfigGenerator(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalGitClient localGitClient,
        IFileSystem fileSystem,
        ILogger<CodeQLConfigGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task UpdateCodeQLConfig(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating {codeQLConfig}...", VmrInfo.CodeQLConfigPath);

        var destinationPath = _vmrInfo.VmrPath / VmrInfo.CodeQLConfigPath;
        bool fileExistedBefore = _fileSystem.FileExists(destinationPath);
        var pathClassifiers = new SortedDictionary<string, List<CodeQLConfigGroup>>(StringComparer.Ordinal);
        var queries = new List<CodeQLConfigGroup>();

        foreach (ISourceComponent component in _sourceManifest.Repositories
            .Cast<ISourceComponent>()
            .Concat(_sourceManifest.Submodules)
            .OrderBy(component => component.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddCodeQLConfigAsync(component, pathClassifiers, queries, cancellationToken);
        }

        if (pathClassifiers.Count == 0 && queries.Count == 0)
        {
            _fileSystem.DeleteFile(destinationPath);
        }
        else
        {
            using var stream = _fileSystem.GetFileStream(destinationPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            WriteCodeQLConfig(writer, pathClassifiers, queries);
        }

        if (fileExistedBefore || _fileSystem.FileExists(destinationPath))
        {
            await _localGitClient.StageAsync(
                _vmrInfo.VmrPath,
                [VmrInfo.CodeQLConfigPath],
                cancellationToken);
        }

        _logger.LogDebug("{codeQLConfig} updated", VmrInfo.CodeQLConfigPath);
    }

    private async Task AddCodeQLConfigAsync(
        ISourceComponent component,
        SortedDictionary<string, List<CodeQLConfigGroup>> pathClassifiers,
        List<CodeQLConfigGroup> queries,
        CancellationToken cancellationToken)
    {
        var componentDirectory = _vmrInfo.VmrPath / VmrInfo.SourcesDir / component.Path;
        foreach (string fileName in s_codeQLConfigFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configPath = componentDirectory / fileName;
            if (!_fileSystem.FileExists(configPath))
            {
                continue;
            }

            string configContent = await _fileSystem.ReadAllTextAsync(configPath);
            var yaml = new YamlStream();
            using (var reader = new StringReader(configContent))
            {
                yaml.Load(reader);
            }

            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                throw new InvalidDataException($"{configPath} must contain one YAML mapping document.");
            }

            string[] lines = configContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            // Only path_classifiers and queries are aggregated; other top-level settings are skipped with a warning.
            foreach ((YamlNode sectionKey, YamlNode section) in root.Children)
            {
                var key = sectionKey as YamlScalarNode;
                if (key?.Value == "path_classifiers")
                {
                    AddPathClassifiers(component, configPath, lines, section, pathClassifiers);
                }
                else if (key?.Value == "queries")
                {
                    AddQueries(component, configPath, lines, key, section, queries);
                }
                else
                {
                    _logger.LogWarning(
                        "Ignoring unrecognized top-level CodeQL key '{key}' in {configPath}. Only path_classifiers and queries are supported.",
                        sectionKey.ToString(),
                        configPath);
                }
            }
        }
    }

    private static void AddPathClassifiers(
        ISourceComponent component,
        string configPath,
        string[] lines,
        YamlNode section,
        SortedDictionary<string, List<CodeQLConfigGroup>> pathClassifiers)
    {
        if (section is not YamlMappingNode classifiers)
        {
            throw new InvalidDataException($"path_classifiers in {configPath} must be a mapping.");
        }

        foreach ((YamlNode classifierNode, YamlNode paths) in classifiers.Children)
        {
            if (classifierNode is not YamlScalarNode { Value: not null } classifier)
            {
                throw new InvalidDataException($"A path classifier in {configPath} has an invalid key.");
            }

            var entries = ReadEntries(lines, classifier, paths, configPath,
                path => PrefixClassifierPathNode(path, component.Path, configPath));
            if (entries.Count == 0)
            {
                continue;
            }

            if (!pathClassifiers.TryGetValue(classifier.Value, out var groups))
            {
                groups = [];
                pathClassifiers.Add(classifier.Value, groups);
            }

            groups.Add(new CodeQLConfigGroup(component, entries));
        }
    }

    private static void AddQueries(
        ISourceComponent component,
        string configPath,
        string[] lines,
        YamlScalarNode key,
        YamlNode section,
        List<CodeQLConfigGroup> queries)
    {
        var entries = ReadEntries(lines, key, section, configPath,
            query => PrefixQueryPaths(query, component.Path, configPath));
        if (entries.Count > 0)
        {
            queries.Add(new CodeQLConfigGroup(component, entries));
        }
    }

    private static YamlNode PrefixPathNode(YamlNode pathNode, string componentPath, string configPath)
    {
        if (pathNode is not YamlScalarNode path || path.Value is null)
        {
            throw new InvalidDataException($"A path in {configPath} must be a scalar.");
        }

        path.Value = PrefixPath(componentPath, path.Value);
        return path;
    }

    private static YamlNode PrefixClassifierPathNode(YamlNode pathNode, string componentPath, string configPath)
    {
        if (pathNode is YamlScalarNode)
        {
            return PrefixPathNode(pathNode, componentPath, configPath);
        }

        if (pathNode is not YamlMappingNode mapping)
        {
            throw new InvalidDataException($"A path classifier entry in {configPath} must be a scalar or mapping.");
        }

        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode || valueNode is not YamlScalarNode)
            {
                throw new InvalidDataException(
                    $"A path classifier mapping entry in {configPath} must have scalar keys and values.");
            }

            PrefixPathNode(valueNode, componentPath, configPath);
        }

        return mapping;
    }

    private static YamlNode PrefixQueryPaths(YamlNode queryNode, string componentPath, string configPath)
    {
        if (queryNode is not YamlMappingNode query)
        {
            return queryNode;
        }

        foreach ((YamlNode keyNode, YamlNode valueNode) in query.Children)
        {
            if (keyNode is not YamlScalarNode key
                || key.Value is not ("exclude" or "include")
                || valueNode is not YamlMappingNode filter)
            {
                continue;
            }

            var pathFilters = filter.Children
                .Where(entry => entry.Key is YamlScalarNode { Value: "path" or "paths" })
                .Select(entry => entry.Value)
                .ToList();
            if (pathFilters.Count == 0)
            {
                // Otherwise a source repo's global filter would apply to unrelated repos in the VMR.
                filter.Add(
                    new YamlScalarNode("path"),
                    new YamlSequenceNode(new YamlScalarNode($"{VmrInfo.SourcesDir}/{componentPath}")));
            }
            foreach (YamlNode paths in pathFilters)
            {
                foreach (YamlNode path in paths is YamlSequenceNode sequence ? sequence.Children : [paths])
                {
                    PrefixPathNode(path, componentPath, configPath);
                }
            }
        }

        return query;
    }

    // Preserve source glob syntax rather than correcting or expanding it during aggregation.
    private static string PrefixPath(string componentPath, string path)
        => $"{VmrInfo.SourcesDir}/{componentPath}/{path.TrimStart('/')}";

    private static List<CodeQLConfigNode> ReadEntries(
        string[] lines,
        YamlScalarNode sectionKey,
        YamlNode section,
        string configPath,
        Func<YamlNode, YamlNode> transform)
    {
        if (section is not YamlSequenceNode sequence)
        {
            throw new InvalidDataException($"{sectionKey.Value} in {configPath} must be a sequence.");
        }

        var entries = sequence.Children.Select(node => new CodeQLConfigNode(node, [])).ToList();
        if (entries.Count == 0)
        {
            return [];
        }

        // YamlDotNet drops comments from its node model, so recover full-line comments from the source.
        // Keep nested explanations with their entry, but emit them above it at a uniform indentation.
        int nextEntry = 0;
        int firstContentLine = checked((int)sectionKey.Start.Line) + 1;
        for (int lineNumber = firstContentLine; lineNumber <= lines.Length; lineNumber++)
        {
            string sourceLine = lines[lineNumber - 1];
            string comment = sourceLine.TrimStart();
            int indentation = sourceLine.Length - comment.Length;
            if (comment.Length == 0)
            {
                continue;
            }
            if (!comment.StartsWith('#'))
            {
                if (indentation < sectionKey.Start.Column)
                {
                    break;
                }
                continue;
            }

            while (nextEntry < entries.Count && entries[nextEntry].Node.Start.Line <= lineNumber)
            {
                nextEntry++;
            }

            // Shallow comments introduce the next entry; deeper ones describe the previous entry.
            // Comments after the last entry stay with that entry.
            int target = nextEntry;
            if (target == entries.Count || (target > 0 && indentation >= entries[target].Node.Start.Column))
            {
                target--;
            }
            entries[target].Comments.Add(comment.TrimEnd());
        }

        return entries.Select(entry => entry with { Node = transform(entry.Node) }).ToList();
    }

    private static void WriteCodeQLConfig(
        TextWriter writer,
        IReadOnlyDictionary<string, List<CodeQLConfigGroup>> pathClassifiers,
        IReadOnlyList<CodeQLConfigGroup> queries)
    {
        var serializer = new SerializerBuilder().Build();
        writer.Write(GeneratedFileHeader);

        if (pathClassifiers.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("path_classifiers:");
            bool firstClassifier = true;
            foreach ((string classifier, List<CodeQLConfigGroup> groups) in pathClassifiers)
            {
                if (!firstClassifier)
                {
                    writer.WriteLine();
                }
                firstClassifier = false;
                writer.WriteLine($"  {serializer.Serialize(classifier).TrimEnd()}:");
                WriteGroups(writer, serializer, groups, "    ");
            }
        }

        if (queries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("queries:");
            WriteGroups(writer, serializer, queries, "  ");
        }
    }

    private static void WriteGroups(
        TextWriter writer,
        ISerializer serializer,
        IReadOnlyList<CodeQLConfigGroup> groups,
        string indentation)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (i > 0)
            {
                writer.WriteLine();
            }
            string repository = GitRepoUrlUtils.GetRepoNameWithOrg(groups[i].Component.RemoteUri);
            writer.WriteLine($"{indentation}# --- {repository} ---");
            foreach (CodeQLConfigNode entry in groups[i].Nodes)
            {
                foreach (string comment in entry.Comments)
                {
                    writer.WriteLine(indentation + comment);
                }

                // Serialize a one-item sequence so YAML handles escaping and nested structure,
                // while we control comments and group spacing outside the serializer.
                using var reader = new StringReader(serializer.Serialize(new YamlSequenceNode(entry.Node)));
                while (reader.ReadLine() is { } line)
                {
                    writer.WriteLine(indentation + line);
                }
            }
        }
    }

    private sealed record CodeQLConfigGroup(ISourceComponent Component, IReadOnlyList<CodeQLConfigNode> Nodes);

    private sealed record CodeQLConfigNode(YamlNode Node, List<string> Comments);
}
