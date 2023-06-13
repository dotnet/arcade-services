// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrPusherTests
{
    private readonly Mock<ISourceManifest> _sourceManifest = new();
    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<ILocalGitRepo> _localGitRepo = new();
    private const string GraphQLUri = "https://api.github.com/graphql";
    private const string Sha = "7cf329817c862c15f9a4e5849b2268d801cb1078";
    private const string VmrUrl = "https://github.com/org/vmr";

    [SetUp]
    public void SetUp()
    {
        var repo = new RepositoryRecord("some-repo", "https://github.com/org/some-repo", Sha, "8.0");

        _sourceManifest.Reset();
        _sourceManifest.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord>() { repo });
    }

    [Test]
    public void PushingUnexistingCommitThrowsExceptionTest()
    {
        var remoteConfiguration = new VmrRemoteConfiguration(null, null);
        var mockHttpClientFactory = new MockHttpClientFactory();

        var responseMsg = "{\"data\":{\"somerepo\":{\"object\":null}}}";
        mockHttpClientFactory.AddCannedResponse(
            GraphQLUri, 
            responseMsg, 
            HttpStatusCode.OK, 
            "application/json", 
            HttpMethod.Post);

        var vmrPusher = new VmrPusher(
            _vmrInfo.Object, 
            new NullLogger<VmrPusher>(), 
            _sourceManifest.Object, 
            mockHttpClientFactory,
            _localGitRepo.Object,
            remoteConfiguration);

        vmrPusher.Awaiting(p => p.Push(VmrUrl, "branch", false, "public-github-pat", CancellationToken.None))
            .Should()
            .Throw<Exception>()
            .WithMessage("Not all pushed commits are publicly available");
    }

    [Test]
    public async Task PublicCommitsArePushedTest()
    {
        LocalPath vmrPath = new NativePath("vmr");

        _vmrInfo.Reset();
        _vmrInfo.SetupGet(i => i.VmrPath).Returns(vmrPath);

        var remoteConfiguration = new VmrRemoteConfiguration("git-hub-pat", "az-do-pat");
        var mockHttpClientFactory = new MockHttpClientFactory();

        var responseMsg = "{\"data\":{\"somerepo\":{\"object\": {\"id\": \"C_kwDOBjr6NNoAKGNjYjQ2YWU5M2E4MjhkYjE4MWIzMTBkZTBkMmIwNTI1MWQ0ZDcxNDA\"}}}}";
        mockHttpClientFactory.AddCannedResponse(
            GraphQLUri,
            responseMsg,
            HttpStatusCode.OK,
            "application/json",
            HttpMethod.Post);

        var vmrPusher = new VmrPusher(
            _vmrInfo.Object,
            new NullLogger<VmrPusher>(),
            _sourceManifest.Object,
            mockHttpClientFactory,
            _localGitRepo.Object,
            remoteConfiguration);

        await vmrPusher.Push(VmrUrl, "branch", false, "public-github-pat", CancellationToken.None);

        _localGitRepo.Verify(
            x => x.Push(
                vmrPath,  
                "branch", 
                VmrUrl,
                "git-hub-pat",
                It.Is<LibGit2Sharp.Identity>(x => x.Name == Constants.DarcBotName && x.Email == Constants.DarcBotEmail)), 
            Times.Once());
    }
}
