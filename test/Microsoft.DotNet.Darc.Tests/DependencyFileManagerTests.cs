// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyFileManagerTests
{
    private const string TestInputsRootDir = "inputs";
    private const string ConfigFilesInput = "NugetConfigFiles";
    private const string VersionPropsFilesInput = "VersionPropsFiles";
    private const string InputNugetConfigFile = "NuGet.input.config";
    private const string OutputNugetConfigFile = "NuGet.output.config";

    [TestCase("RemoveAllManagedFeeds", new string[0])]
    [TestCase("AddFeedsToNuGetConfigWithoutManagedFeeds", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("MatchMoreFeedPatterns", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json",
        // Both forms of the internal feeds (org and internal project)
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-runtime-5a18de8a/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/internal/_packaging/darc-int-dotnet-aspnetcore-10cdf3ba/nuget/v3/index.json" })]
    [TestCase("ReplaceExistingFeedsWithNewOnes", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json" })]
    [TestCase("PreserveCommentsInRightLocationsWhenReplacing", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("PreserveCommentsInRightLocationsWithNoExistingBlock", new string[] {
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("PreserveManagedFeedFromOutside", new string[] {
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("WhiteSpaceCorrectlyFormatted", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-core-setup-7d57652f/nuget/v3/index.json" })]
    [TestCase("NonManagedDisabledFeedSources", new string[0])]  // Don't do anything to normal disabled sources
    [TestCase("RedisableCurrentlyEnabledIntSources", new string[] { // Flip enabled int source to disabled.
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json" })]
    [TestCase("EnsureAppendsDisableEntryAfterLastClear", new string[] { // Honor all existing disable entries and append after last found <clear/>
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json" })]
    [TestCase("EnsureMultipleDisabledSourcesGetComments", new string[] { // If there are more than one disabled source, all need comments
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-runtime-c13a8a85/nuget/v3/index.json" })]
    [TestCase("RemovingAllSourcesKeepsTheComments", new string[] { })] // Make sure we always preserve comments even with no feeds.
    [TestCase("MalformedMaestroManagedFeedComentsJustBegin", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("MalformedMaestroManagedFeedComentsJustEnd", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    [TestCase("ExistingFeedsHaveCommentsOutOfAlphabeticOrder", new string[] {
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
        "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
    public async Task UpdatePackageSourcesTests(string testName, string[] managedFeeds)
    {
        var dependencyFileManager = new DependencyFileManager((IGitRepo)null, new VersionDetailsParser(), NullLogger.Instance);

        string inputNugetPath = Path.Combine(
            Environment.CurrentDirectory,
            TestInputsRootDir,
            ConfigFilesInput,
            testName,
            InputNugetConfigFile);
        string inputXmlContent = await File.ReadAllTextAsync(inputNugetPath);
        var inputNuGetConfigFile = DependencyFileManager.GetXmlDocument(inputXmlContent);

        var configFileUpdateData = new Dictionary<string, HashSet<string>>
        {
            { "testKey", new HashSet<string>(managedFeeds) }
        };
        var managedFeedsForTest = dependencyFileManager.FlattenLocationsAndSplitIntoGroups(configFileUpdateData);

        // 'unknown' = regex failed to match and extract repo name from feed
        managedFeedsForTest.Keys.Should().NotContain("unknown");

        XmlDocument updatedConfigFile =
            dependencyFileManager.UpdatePackageSources(inputNuGetConfigFile, managedFeedsForTest);

        var outputNugetPath = Path.Combine(
            Environment.CurrentDirectory,
            TestInputsRootDir,
            ConfigFilesInput,
            testName,
            OutputNugetConfigFile);
        string expectedOutputText = await File.ReadAllTextAsync(outputNugetPath);

        // Dump the output xml using the git file manager
        var file = new GitFile(null, updatedConfigFile);

        // Normalize the \r\n newlines in the expected output to \n if they
        // exist (GitFile normalizes these before writing)
        expectedOutputText = expectedOutputText.Replace(Environment.NewLine, "\n");

        file.Content.Should().Be(expectedOutputText);

        // When this is performed via the Maestro service instead of the Darc CLI, it seemingly can 
        // be run more than once for the same XmlDocument.  This should not impact the contents of the file; 
        // Validate this expectation of idempotency by running the same update on the resultant file.
        XmlDocument doubleUpdatedConfigFile = dependencyFileManager.UpdatePackageSources(updatedConfigFile, managedFeedsForTest);
        var doubleUpdatedfile = new GitFile(null, doubleUpdatedConfigFile);
        doubleUpdatedfile.Content.Should().Be(expectedOutputText, "Repeated invocation of UpdatePackageSources() caused incremental changes to nuget.config");
    }

    [TestCase("SimpleDuplicated.props", true)]
    [TestCase("DuplicatedSameConditions.props", true)]
    [TestCase("AlternateNamesDuplicated.props", true)]
    [TestCase("NothingDuplicated.props", false)]
    [TestCase("NoDuplicatedDifferentConditions.props", false)]
    public void VerifyNoDuplicatedPropertiesTests(string inputFileName, bool hasDuplicatedProps)
    {
        var dependencyFileManager = new DependencyFileManager((IGitRepo)null, new VersionDetailsParser(), NullLogger.Instance);

        string inputVersionPropsPath = Path.Combine(
            Environment.CurrentDirectory,
            TestInputsRootDir,
            VersionPropsFilesInput,
            inputFileName);

        string propsFileContent = File.ReadAllText(inputVersionPropsPath);

        XmlDocument propsFile = DependencyFileManager.GetXmlDocument(propsFileContent);

        dependencyFileManager.VerifyNoDuplicatedProperties(propsFile).Result.Should().Be(!hasDuplicatedProps);
    }

}
