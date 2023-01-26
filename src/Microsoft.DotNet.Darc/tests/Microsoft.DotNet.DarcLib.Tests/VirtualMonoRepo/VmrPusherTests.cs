// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
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
    private const string GraphQLUri = "https://api.github.com/graphql";
    private const string Sha = "7cf329817c862c15f9a4e5849b2268d801cb1078";

    [Test]
    public void PushingUnexistingCommitThrowsExceptionTest()
    {
        var repo = new RepositoryRecord("some-repo", "https://github.com/org/some-repo", Sha, "8.0");

        _sourceManifest.Reset();
        _sourceManifest.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord>() { repo});

        var _remoteConfiguration = new VmrRemoteConfiguration(null, null);
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
            _remoteConfiguration);

        vmrPusher.Awaiting(p => p.Push("remote", "branch", true, "public-github-pat", CancellationToken.None))
            .Should()
            .Throw<Exception>()
            .WithMessage("Not all pushed commits are publicly available");
    }
}
