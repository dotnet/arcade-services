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
        const string InputNugetConfigFile = "NuGet.input.config";
        const string OutputNugetConfigFile = "NuGet.output.config";

        [Theory]
        [InlineData("NoManagedFeedsToAdd", new string[0])]
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
        [InlineData("RemoveManagedFromFromOutside", new string[] {
            "https://pkgs.dev.azure.com/dnceng/_packaging/darc-int-dotnet-arcade-b0437974/nuget/v3/index.json",
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-corefx-4ac4c036/nuget/v3/index.json" })]
        public async Task UpdatePackageSourcesTests(string testName, string[] managedFeeds)
        {
            GitFileManager gitFileManager = new GitFileManager(null, NullLogger.Instance);

            string inputNugetPath = Path.Combine(
                Environment.CurrentDirectory,
                TestInputsRootDir,
                ConfigFilesInput,
                testName,
                InputNugetConfigFile);
            XmlDocument inputNuGetConfigFile = new XmlDocument();
            inputNuGetConfigFile.Load(inputNugetPath);

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
            string actualOutputText = GitFile.GetIndentedXmlBody(updatedConfigFile);

            Assert.Equal(expectedOutputText, actualOutputText);
        }
    }
}
