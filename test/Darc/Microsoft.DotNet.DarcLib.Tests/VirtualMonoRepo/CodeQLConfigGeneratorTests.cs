// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class CodeQLConfigGeneratorTests
{
    private NativePath _vmrPath = null!;
    private Mock<ILocalGitClient> _localGitClient = null!;
    private Mock<ILogger<CodeQLConfigGenerator>> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _vmrPath = new NativePath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        Directory.CreateDirectory(_vmrPath);
        _localGitClient = new Mock<ILocalGitClient>();
        _logger = new Mock<ILogger<CodeQLConfigGenerator>>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_vmrPath))
        {
            Directory.Delete(_vmrPath, recursive: true);
        }
    }

    [Test]
    public async Task UpdateCodeQLConfig_MergesAndScopesRepositoryConfigs()
    {
        var sourceManifest = new SourceManifest(
        [
            new RepositoryRecord("zeta", "https://github.com/dotnet/zeta", "sha", null),
            new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null),
        ],
        [
            new SubmoduleRecord(
                "runtime/externals/library",
                "https://github.com/vendor/library",
                "sha"),
        ]);

        WriteConfig(
            "runtime",
            ".CodeQL.yml",
            """
            path_classifiers:
              refs:
                - "src/libraries/**/ref/*"
              generated:
                - "artifacts/**"
                - exclude: "artifacts/custom"
            queries:
              - exclude:
                  queryid:
                    - "cs/duplicate"
            """);
        WriteConfig(
            "runtime",
            "CodeQL.yml",
            """
            path_classifiers:
              refs:
                - "eng/generated/**"
            """);
        WriteConfig(
            "runtime/externals/library",
            "CodeQL.yml",
            """
            path_classifiers:
              refs:
                - "generated/**"
            queries:
              - exclude:
                  queryid:
                    - "cs/duplicate"
            """);
        WriteConfig(
            "zeta",
            ".CodeQL.yml",
            """
            path_classifiers:
              refs:
                - "artifacts/obj/**/CMakeFiles/**"
            """);

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        string output = await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath);
        output.Should().Contain("\n    # --- vendor/library ---");
        output.Should().Contain("\n  # --- vendor/library ---");
        var root = ParseYaml(output);
        var classifiers = (YamlMappingNode)root["path_classifiers"];
        var refs = (YamlSequenceNode)classifiers["refs"];
        refs.Children.Select(node => ((YamlScalarNode)node).Value).Should().Equal(
            "src/runtime/src/libraries/**/ref/*",
            "src/runtime/eng/generated/**",
            "src/runtime/externals/library/generated/**",
            "src/zeta/artifacts/obj/**/CMakeFiles/**");
        var generated = (YamlSequenceNode)classifiers["generated"];
        ((YamlScalarNode)generated.Children[0]).Value.Should().Be("src/runtime/artifacts/**");
        var classifierExclusion = (YamlMappingNode)generated.Children[1];
        ((YamlScalarNode)classifierExclusion["exclude"]).Value
            .Should().Be("src/runtime/artifacts/custom");
        var queries = (YamlSequenceNode)root["queries"];
        queries.Children.Cast<YamlMappingNode>()
            .Select(query =>
            {
                var filter = (YamlMappingNode)query["exclude"];
                return (
                    ((YamlScalarNode)((YamlSequenceNode)filter["queryid"]).Children.Single()).Value,
                    ((YamlScalarNode)((YamlSequenceNode)filter["path"]).Children.Single()).Value);
            })
            .Should().Equal(
                ("cs/duplicate", "src/runtime"),
                ("cs/duplicate", "src/runtime/externals/library"));

        _localGitClient.Verify(
            client => client.StageAsync(
                _vmrPath,
                It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { (string)VmrInfo.CodeQLConfigPath })),
                CancellationToken.None),
            Times.Once);
    }

    [TestCase("exclude", "queryid", "", "path: [src/runtime]")]
    [TestCase("include", "queryid", "", "path: [src/runtime]")]
    [TestCase("exclude", "opaqueid", "", "path: [src/runtime]")]
    [TestCase("include", "queryid", "path: src/**", "path: src/runtime/src/**")]
    [TestCase("exclude", "queryid", "path: [artifacts/obj/**, /eng/**]", "path: [src/runtime/artifacts/obj/**, src/runtime/eng/**]")]
    [TestCase("include", "queryid", "paths: src/**", "paths: src/runtime/src/**")]
    [TestCase("exclude", "queryid", "paths: [src/**]", "paths: [src/runtime/src/**]")]
    public async Task UpdateCodeQLConfig_ScopesQueryFilters(
        string action, string idKey, string sourcePath, string expectedPath)
    {
        var sourceManifest = new SourceManifest(
            [new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null)],
            []);
        WriteConfig("runtime", ".CodeQL.yml",
            $"""
            queries:
              - {action}:
                  {idKey}: [example]
                  {sourcePath}
            """);

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        var root = ParseYaml(await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath));
        var query = (YamlMappingNode)((YamlSequenceNode)root["queries"]).Children.Single();
        query.Children.Keys.Should().Equal(new YamlScalarNode(action));
        query[action].Should().Be(ParseYaml($"{idKey}: [example]\n{expectedPath}"));
    }

    [Test]
    public async Task UpdateCodeQLConfig_UsesManifestRepositoryForMarkersAndMappingForPaths()
    {
        var sourceManifest = new SourceManifest(
            [new RepositoryRecord("nuget-client", "https://github.com/nuget/nuget.client", "sha", null)],
            []);
        WriteConfig("nuget-client", ".CodeQL.yml",
            """
            path_classifiers:
              refs:
                - "ref/*"
            queries:
              - exclude:
                  queryid:
                    - "cs/example"
              - include:
                  queryid:
                    - "cs/path-specific"
                  path: "src/**"
            """);

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        string output = await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath);
        output.Should().Contain("    # --- nuget/nuget.client ---");
        output.Should().Contain("\n  # --- nuget/nuget.client ---");
        output.Should().NotContain("dotnet/nuget-client");
        var root = ParseYaml(output);
        var classifiers = (YamlMappingNode)root["path_classifiers"];
        ((YamlScalarNode)((YamlSequenceNode)classifiers["refs"]).Children.Single()).Value
            .Should().Be("src/nuget-client/ref/*");
        var queries = (YamlSequenceNode)root["queries"];
        var exclude = (YamlMappingNode)((YamlMappingNode)queries.Children[0])["exclude"];
        ((YamlScalarNode)((YamlSequenceNode)exclude["path"]).Children.Single()).Value
            .Should().Be("src/nuget-client");
        var include = (YamlMappingNode)((YamlMappingNode)queries.Children[1])["include"];
        ((YamlScalarNode)include["path"]).Value.Should().Be("src/nuget-client/src/**");
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task UpdateCodeQLConfig_PreservesCommentPlacementAndGroupSpacing(string lineEnding)
    {
        var sourceManifest = new SourceManifest(
        [
            new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null),
            new RepositoryRecord("winforms", "https://github.com/dotnet/winforms", "sha", null),
        ], []);
        WriteConfig("runtime", ".CodeQL.yml",
            """
            path_classifiers:
              refs:
                # Reference sources are not shipping implementations.
                - "src/ref/*"
              generated:
                # Generated code.
                - "artifacts/**"
            queries:
              # Repository-wide exclusions.
              - exclude:
                  queryid:
                    # Handled by another analyzer.
                    - "cs/runtime"
              # Only include this query for matching paths.
              - include:
                  queryid:
                    - "cs/shared"
                  paths:
                    - "src/**"
            """.ReplaceLineEndings(lineEnding));
        WriteConfig("winforms", ".CodeQL.yml",
            """
            path_classifiers:
              refs:
                # Another repository's reference sources.
                - "ref/*"
            queries:
              - exclude:
                  queryid:
                    # Keep this explanation with the single query.
                    - "cs/winforms"
            """.ReplaceLineEndings(lineEnding));

        var generator = CreateGenerator(sourceManifest);
        await generator.UpdateCodeQLConfig(CancellationToken.None);

        string output = await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath);
        output.ReplaceLineEndings("\n").Should().Be(
            """
            # This file is auto-generated from .CodeQL.yml and CodeQL.yml files in src/.
            # Manual changes will be overwritten.

            path_classifiers:
              generated:
                # --- dotnet/runtime ---
                # Generated code.
                - "src/runtime/artifacts/**"

              refs:
                # --- dotnet/runtime ---
                # Reference sources are not shipping implementations.
                - "src/runtime/src/ref/*"

                # --- dotnet/winforms ---
                # Another repository's reference sources.
                - "src/winforms/ref/*"

            queries:
              # --- dotnet/runtime ---
              # Repository-wide exclusions.
              # Handled by another analyzer.
              - exclude:
                  queryid:
                  - "cs/runtime"
                  path:
                  - src/runtime
              # Only include this query for matching paths.
              - include:
                  queryid:
                  - "cs/shared"
                  paths:
                  - "src/runtime/src/**"

              # --- dotnet/winforms ---
              # Keep this explanation with the single query.
              - exclude:
                  queryid:
                  - "cs/winforms"
                  path:
                  - src/winforms
            """ + "\n");

        await generator.UpdateCodeQLConfig(CancellationToken.None);
        (await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath)).Should().Be(output);
    }

    [Test]
    public async Task UpdateCodeQLConfig_WarnsForUnrecognizedKeysAndKeepsSupportedSections()
    {
        var sourceManifest = new SourceManifest(
            [new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null)],
            []);
        WriteConfig("runtime", ".CodeQL.yml",
            """
            name: custom
            path_classifiers:
              refs:
                - "ref/*"
            paths-ignore:
              - tests
            queries:
              - exclude:
                  queryid:
                    - "cs/example"
            """);

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        string configPath = _vmrPath / VmrInfo.SourcesDir / "runtime" / ".CodeQL.yml";
        var warnings = _logger.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log)
                && invocation.Arguments[0] is LogLevel.Warning)
            .Select(invocation => invocation.Arguments[2].ToString())
            .ToList();
        warnings.Should().HaveCount(2);
        warnings[0].Should().Contain("'name'").And.Contain(configPath);
        warnings[1].Should().Contain("'paths-ignore'").And.Contain(configPath);

        var root = ParseYaml(await File.ReadAllTextAsync(_vmrPath / VmrInfo.CodeQLConfigPath));
        root.Children.Should().Equal(ParseYaml(
            """
            path_classifiers:
              refs: [src/runtime/ref/*]
            queries:
              - exclude:
                  queryid: [cs/example]
                  path: [src/runtime]
            """).Children);
    }

    [Test]
    public async Task UpdateCodeQLConfig_IgnoresUnrelatedAndNestedFiles()
    {
        var sourceManifest = new SourceManifest(
            [new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null)],
            []);
        WriteConfig("runtime", "OtherConfig.yml", "path_classifiers:\n  ignored:\n    - unrelated/**");
        WriteConfig("runtime/eng", ".CodeQL.yml", "path_classifiers:\n  ignored:\n    - nested/**");

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        File.Exists(_vmrPath / VmrInfo.CodeQLConfigPath).Should().BeFalse();
        _localGitClient.Verify(
            client => client.StageAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task UpdateCodeQLConfig_DeletesAndStagesExistingOutputWhenConfigsAreRemoved()
    {
        var sourceManifest = new SourceManifest(
            [new RepositoryRecord("runtime", "https://github.com/dotnet/runtime", "sha", null)],
            []);
        Directory.CreateDirectory(_vmrPath / VmrInfo.SourcesDir / "runtime");
        File.WriteAllText(_vmrPath / VmrInfo.CodeQLConfigPath, "queries: []");

        await CreateGenerator(sourceManifest).UpdateCodeQLConfig(CancellationToken.None);

        File.Exists(_vmrPath / VmrInfo.CodeQLConfigPath).Should().BeFalse();
        _localGitClient.Verify(
            client => client.StageAsync(
                _vmrPath,
                It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { (string)VmrInfo.CodeQLConfigPath })),
                CancellationToken.None),
            Times.Once);
    }

    private CodeQLConfigGenerator CreateGenerator(ISourceManifest sourceManifest)
    {
        var vmrInfo = Mock.Of<IVmrInfo>(info => info.VmrPath == _vmrPath);
        return new CodeQLConfigGenerator(
            vmrInfo,
            sourceManifest,
            _localGitClient.Object,
            new FileSystem(),
            _logger.Object);
    }

    private void WriteConfig(string componentPath, string fileName, string content)
    {
        var directory = _vmrPath / VmrInfo.SourcesDir / componentPath;
        Directory.CreateDirectory(directory);
        File.WriteAllText(directory / fileName, content);
    }

    private static YamlMappingNode ParseYaml(string content)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(content);
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents.Single().RootNode;
    }
}
