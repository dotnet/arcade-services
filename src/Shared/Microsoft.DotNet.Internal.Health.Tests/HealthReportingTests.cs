// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

            async Task SaveRequest(HttpRequestMessage tableReplaceRequest)
            {
                tableRequestUri = tableReplaceRequest.RequestUri;
                tableRequestBody = JToken.Parse(await tableReplaceRequest.Content.ReadAsStringAsync());
            }

            var handler = new MockHandler(async req =>
            {
                await SaveRequest(req);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

            var collection = new ServiceCollection();
            collection.AddSingleton<IHttpClientFactory>(handler);
            collection.AddSingleton<IHttpMessageHandlerFactory>(handler);
            collection.AddHttpClient();
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
        
        [Test]
        public async Task ReadSimpleHealthResultFromTableEndpoint()
        {
            Uri tableRequestUri = null;

            void SaveRequest(HttpRequestMessage tableReplaceRequest)
            {
                tableRequestUri = tableReplaceRequest.RequestUri;
            }

            var handler = new MockHandler(req =>
            {
                SaveRequest(req);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        @"{""Timestamp"":""2001-02-03T16:05:06.007Z"",""Status"":""Healthy"",""Message"":""TEST STATUS MESSAGES""}")
                    {
                        Headers = {ContentType = MediaTypeHeaderValue.Parse("application/json")}
                    }
                };
                return Task.FromResult(response);
            });

            var collection = new ServiceCollection();
            collection.AddSingleton<IHttpClientFactory>(handler);
            collection.AddSingleton<IHttpMessageHandlerFactory>(handler);
            collection.AddHttpClient();
            collection.AddHealthReporting(b =>
            {
                b.AddAzureTable("http://tables.example.test/myTable?someQueryStuff");
            });
            collection.AddLogging(b => b.AddProvider(new NUnitLogger()));

            await using ServiceProvider services = collection.BuildServiceProvider();
            var provider = services.GetRequiredService<AzureTableHealthReportProvider>();

            HealthReport report = await provider.GetStatusAsync(GetType().FullName, "TEST-SUB-STATUS");

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
            }
            
            report.ServiceName.Should().Be(typeof(HealthReportingTests).FullName);
            report.SubStatusName.Should().Be("TEST-SUB-STATUS");
            report.Health.Should().Be(HealthStatus.Healthy);
            report.Message.Should().Be("TEST STATUS MESSAGES");
            report.AsOf.Should().Be(new DateTimeOffset(2001, 2, 3, 16, 5, 6, 7, TimeSpan.Zero));
        }

        [Test]
        public async Task MissingRowReturnsUnknownResult()
        {
            var handler = new MockHandler(req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var collection = new ServiceCollection();
            collection.AddSingleton<IHttpClientFactory>(handler);
            collection.AddSingleton<IHttpMessageHandlerFactory>(handler);
            collection.AddHttpClient();
            collection.AddHealthReporting(b =>
            {
                b.AddAzureTable("http://tables.example.test/myTable?someQueryStuff");
            });
            collection.AddLogging(b => b.AddProvider(new NUnitLogger()));

            await using ServiceProvider services = collection.BuildServiceProvider();
            var provider = services.GetRequiredService<AzureTableHealthReportProvider>();

            HealthReport report = await provider.GetStatusAsync(GetType().FullName, "TEST-SUB-STATUS");
            
            report.ServiceName.Should().Be(typeof(HealthReportingTests).FullName);
            report.SubStatusName.Should().Be("TEST-SUB-STATUS");
            report.Health.Should().Be(HealthStatus.Unknown);
        }
    }
}
