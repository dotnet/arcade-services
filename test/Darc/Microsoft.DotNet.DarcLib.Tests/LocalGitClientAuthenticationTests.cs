// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class LocalGitClientAuthenticationTests
{
    [TestCase("ghs_installation-token")]
    [TestCase("ghp_personal-access-token")]
    public async Task AddGitAuthHeaderUsesGitHubTokenUsername(string token)
    {
        const string repository = "https://github.com/dotnet/dotnet";
        Mock<IRemoteTokenProvider> tokenProvider = new();
        tokenProvider
            .Setup(provider => provider.GetTokenForRepositoryAsync(repository))
            .ReturnsAsync(token);
        var client = new LocalGitClient(
            tokenProvider.Object,
            new NoTelemetryRecorder(),
            Mock.Of<IProcessManager>(),
            Mock.Of<IFileSystem>(),
            NullLogger<LocalGitClient>.Instance);
        List<string> arguments = [];
        Dictionary<string, string> environmentVariables = [];

        await client.AddGitAuthHeader(arguments, environmentVariables, repository);

        arguments.Should().ContainSingle()
            .Which.Should().Be("--config-env=http.extraheader=GIT_REMOTE_PAT");
        environmentVariables.Should().ContainKey("GIT_REMOTE_PAT");
        string encodedCredentials = environmentVariables["GIT_REMOTE_PAT"]["Authorization: Basic ".Length..];
        string credentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        credentials.Should().Be($"x-access-token:{token}");
        environmentVariables.Should().Contain("GIT_TERMINAL_PROMPT", "0");
    }
}
