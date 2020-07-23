// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Json;
using FluentAssertions.Execution;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Health.Tests
{
    public class HealthReportingTests
    {
        [Test]
        public async Task SimpleHealthReportReportsToTableEndpoint()
        {
            Uri tableRequestUri = null;
            JToken tableRequestBody = null;

            async Task CheckRequest(HttpRequestMessage tableReplaceRequest)
            {
                tableRequestUri = tableReplaceRequest.RequestUri;
                tableRequestBody = JToken.Parse(await tableReplaceRequest.Content.ReadAsStringAsync());
            }

            var handler = new MockHandler(async req =>
            {
                await CheckRequest(req);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

            var collection = new ServiceCollection();
            collection.AddSingleton<IHttpClientFactory>(handler);
            collection.AddSingleton<IHttpMessageHandlerFactory>(handler);
            collection.AddHealthReporting(b =>
            {
                b.AddAzureTable("http://tables.example.test/myTable?someQueryStuff");
            });
            collection.AddLogging(b => b.AddProvider(new NUnitLogger()));

            await using ServiceProvider services = collection.BuildServiceProvider();
            var report = services.GetRequiredService<IHealthReport<HealthReportingTests>>();

            await report.UpdateStatus("TEST-SUB-STATUS", HealthStatus.Healthy, "TEST STATUS MESSAGES");

            tableRequestUri.Should().NotBeNull();
            tableRequestBody.Should().NotBeNull();

            using (new AssertionScope())
            {
                tableRequestUri.Host.Should().Be("tables.example.test");
                tableRequestUri.Scheme.Should().Be("http");
                tableRequestUri.AbsolutePath.Should().StartWith("/myTable");
                tableRequestUri.Query.Should().Be("?someQueryStuff");
                var ex = new Regex(@"\(\s*PartitionKey\s*=\s*'(.*)'\s*,\s*RowKey\s*=\s*'(.*)'\s*\)");
                tableRequestUri.AbsolutePath.Should().MatchRegex(ex);
                Match match = ex.Match(tableRequestUri.AbsolutePath);
                string partitionKey = match.Groups[1].Value;
                string rowKey = match.Groups[2].Value;
                partitionKey.Should().Be(typeof(HealthReportingTests).FullName);
                rowKey.Should().Be("TEST-SUB-STATUS");

                tableRequestBody.Should()
                    .BeEquivalentTo(new JObject
                    {
                        {"Status", "Healthy"},
                        {"Message", "TEST STATUS MESSAGES"},
                    });
            }
        }
    }
}
