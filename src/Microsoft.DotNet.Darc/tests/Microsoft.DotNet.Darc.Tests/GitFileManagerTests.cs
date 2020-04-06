// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests
{
    public class GitFileManagerTests
    {
        const string TestInputsRootDir = "inputs";
        const string ConfigFilesInput = "NugetConfigFiles";
        const string VersionPropsFilesInput = "VersionPropsFiles";
        const string InputNugetConfigFile = "NuGet.input.config";
        const string OutputNugetConfigFile = "NuGet.output.config";

        [Theory]
        [InlineData("RemoveAllManagedFeeds", new string[0])]
        [InlineData("AddFeedsToNuGetConfigWithoutManagedFeeds", new string[] {
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
        // Replaced an existing set of sources (one of which uses an a different URL format)
        // with a second set of feeds
        [InlineData("ReplaceExistingFeedsWithNewOnes", new string[] {
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json" })]
        [InlineData("PreserveCommentsInRightLocationsWhenReplacing", new string[] {
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-standard-a5b5f2e1/nuget/v3/index.json",
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
        [InlineData("PreserveCommentsInRightLocationsWithNoExistingBlock", new string[] {
            "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
        [InlineData("PreserveManagedFromFromOutside", new string[] {
            "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
        [InlineData("WhiteSpaceCorrectlyFormatted", new string[] {
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-core-setup-7d57652f/nuget/v3/index.json" })]
        public async Task UpdatePackageSourcesTests(string testName, string[] managedFeeds)
        {
            GitFileManager gitFileManager = new GitFileManager(null, NullLogger.Instance);

            string inputNugetPath = Path.Combine(
                Environment.CurrentDirectory,
                TestInputsRootDir,
                ConfigFilesInput,
                testName,
                InputNugetConfigFile);
            string inputXmlContent = await File.ReadAllTextAsync(inputNugetPath);
            var inputNuGetConfigFile = GitFileManager.ReadXmlFile(inputXmlContent);

            XmlDocument updatedConfigFile =
                gitFileManager.UpdatePackageSources(inputNuGetConfigFile, new HashSet<string>(managedFeeds));

            var outputNugetPath = Path.Combine(
                Environment.CurrentDirectory,
                TestInputsRootDir,
                ConfigFilesInput,
                testName,
                OutputNugetConfigFile);
            string expectedOutputText = await File.ReadAllTextAsync(outputNugetPath);

            // Dump the output xml using the git file manager
            GitFile file = new GitFile(null, updatedConfigFile);

            // Normalize the \r\n newlines in the expected output to \n if they
            // exist (GitFile normalizes these before writing)
            expectedOutputText = expectedOutputText.Replace(Environment.NewLine, "\n");

            Assert.Equal(expectedOutputText, file.Content);
        }

        [Theory]
        [InlineData("SimpleDuplicated.props", true)]
        [InlineData("DuplicatedSameConditions.props", true)]
        [InlineData("AlternateNamesDuplicated.props", true)]
        [InlineData("NothingDuplicated.props", false)]
        [InlineData("NoDuplicatedDifferentConditions.props", false)]
        public void VerifyNoDuplicatedPropertiesTests(string inputFileName, bool hasDuplicatedProps)
        {
            GitFileManager gitFileManager = new GitFileManager(null, NullLogger.Instance);

            string inputVersionPropsPath = Path.Combine(
                Environment.CurrentDirectory,
                TestInputsRootDir,
                VersionPropsFilesInput,
                inputFileName);

            string propsFileContent = File.ReadAllText(inputVersionPropsPath);

            XmlDocument propsFile = GitFileManager.GetXmlDocument(propsFileContent);

            Assert.Equal(!hasDuplicatedProps, 
                gitFileManager.VerifyNoDuplicatedProperties(propsFile).Result);
        }

    }
}
