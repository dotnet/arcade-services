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

namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrPusherTests
{
    private readonly Mock<ISourceManifest> _sourceManifest = new();
    private const string GraphQLUri = "https://api.github.com/graphql";

    [Test]
    public void PushingUnexistingCommitThrowsExceptionTest()
    {
        _sourceManifest.Reset();
        _sourceManifest.SetupGet(s => s.Repositories).Returns(new List<RepositoryRecord>());

        var mockHttpClientFactory = new MockHttpClientFactory();

        var responseMsg = "{\"data\":{\"somerepo\":{\"object\":null}}}";
        mockHttpClientFactory.AddCannedResponse(GraphQLUri, responseMsg, HttpStatusCode.OK, "application/json", HttpMethod.Post);

        var vmrPusher = new VmrPusher(null, new NullLogger<VmrPusher>(), _sourceManifest.Object, mockHttpClientFactory, null);

        vmrPusher.Awaiting(p => p.Push(It.IsAny<string>(), It.IsAny<string>(), true, It.IsNotNull<string>(), It.IsAny<CancellationToken>()))
            .Should()
            .Throw<Exception>()
            .WithMessage("Not all pushed commits are publicly available");
    }
}
