using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using AwesomeAssertions;
using Kusto.Ingest;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Providers;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility.Azure;
using Moq;
using NUnit.Framework;

namespace Helix.Internal.KnownIssues.Tests
{
    [TestFixture]
    public class KnownIssuesHistoryProviderTests
    {
        [Test]
        public void NormalizeIssueId()
        {
            string normalizedIssue = KnownIssuesHistoryProvider.NormalizeIssueId("dotnet/arcade", 12345);
            normalizedIssue.Should().Be("dotnet.arcade.12345");
        }
    }
}
