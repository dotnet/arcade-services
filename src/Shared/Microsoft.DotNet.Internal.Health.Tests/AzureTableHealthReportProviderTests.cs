// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Json;
using FluentAssertions.Execution;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Match = System.Text.RegularExpressions.Match;

namespace Microsoft.DotNet.Internal.Health.Tests
{
    public class AzureTableHealthReportProviderTests
    {
        private const string TestInstanceName = "TEST-INSTANCE";
        private static readonly Uri TableUri = new Uri("http://tables.example.test/myTable?someQueryStuff");

        [Test]
        public async Task ServiceHealthReportReportsToTableEndpoint()
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
            
            await using ServiceProvider services = BuildServiceProvider(handler);
            var report = services.GetRequiredService<IServiceHealthReporter<AzureTableHealthReportProviderTests>>();

            await report.UpdateStatusAsync("TEST/SUB-STATUS", HealthStatus.Healthy, "TEST STATUS MESSAGES");

            tableRequestUri.Should().NotBeNull();
            tableRequestBody.Should().NotBeNull();

            using (new AssertionScope())
            {
                tableRequestUri.Host.Should().Be(TableUri.Host);
                tableRequestUri.Scheme.Should().Be(TableUri.Scheme);
                tableRequestUri.AbsolutePath.Should().StartWith(TableUri.AbsolutePath);
                tableRequestUri.Query.Should().Be(TableUri.Query);
                var ex = new Regex(@"\(\s*PartitionKey\s*=\s*'(.*)'\s*,\s*RowKey\s*=\s*'(.*)'\s*\)");
                tableRequestUri.AbsolutePath.Should().MatchRegex(ex);
                Match match = ex.Match(tableRequestUri.AbsolutePath);
                string partitionKey = Uri.UnescapeDataString(match.Groups[1].Value);
                string rowKey = Uri.UnescapeDataString(match.Groups[2].Value);
                partitionKey.Should().Be(GetType().FullName);
                rowKey.Should().Be("|TEST:slash:SUB-STATUS");

                tableRequestBody.Should()
                    .BeEquivalentTo(new JObject
                    {
                        {"Status", "Healthy"},
                        {"Message", "TEST STATUS MESSAGES"},
                    });
            }
        }

        [Test]
        public async Task InstanceHealthReportReportsToTableEndpoint()
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
            
            await using ServiceProvider services = BuildServiceProvider(handler);
            var report = services.GetRequiredService<IInstanceHealthReporter<AzureTableHealthReportProviderTests>>();

            await report.UpdateStatusAsync("TEST/SUB-STATUS", HealthStatus.Healthy, "TEST STATUS MESSAGES");

            tableRequestUri.Should().NotBeNull();
            tableRequestBody.Should().NotBeNull();

            using (new AssertionScope())
            {
                tableRequestUri.Host.Should().Be(TableUri.Host);
                tableRequestUri.Scheme.Should().Be(TableUri.Scheme);
                tableRequestUri.AbsolutePath.Should().StartWith(TableUri.AbsolutePath);
                tableRequestUri.Query.Should().Be(TableUri.Query);
                var ex = new Regex(@"\(\s*PartitionKey\s*=\s*'(.*)'\s*,\s*RowKey\s*=\s*'(.*)'\s*\)");
                tableRequestUri.AbsolutePath.Should().MatchRegex(ex);
                Match match = ex.Match(tableRequestUri.AbsolutePath);
                string partitionKey = Uri.UnescapeDataString(match.Groups[1].Value);
                string rowKey = Uri.UnescapeDataString(match.Groups[2].Value);
                partitionKey.Should().Be(GetType().FullName);
                rowKey.Should().Be($"{TestInstanceName}|TEST:slash:SUB-STATUS");

                tableRequestBody.Should()
                    .BeEquivalentTo(new JObject
                    {
                        {"Status", "Healthy"},
                        {"Message", "TEST STATUS MESSAGES"},
                    });
            }
        }
        
        /// <summary>
        /// This functionality is bit implementation specific, but we need to make sure we handle weird values
        /// because AzureTables doesn't allow for some characters
        /// </summary>
        /// <param name="input">String to check in all escaped strings</param>
        [TestCase("basic")]
        [TestCase("with space")]
        [TestCase("with:colon")]
        [TestCase("with/slash")]
        [TestCase("with\\backslash")]
        [TestCase("with#hash")]
        [TestCase("with?question")]
        [TestCase("with|pipe")]
        [TestCase("with:colon:pre-escaped colon (implementation dependent)")]
        public async Task CheckEscaping(string input)
        {
            string partitionKey = null, rowKey = null;
            List<string> requestPaths = new List<string>();
            List<Func<HttpResponseMessage>> responses = new List<Func<HttpResponseMessage>>
            {
                () => new HttpResponseMessage(HttpStatusCode.NoContent),
                () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $@"{{""value"":[
{{""PartitionKey"":""{partitionKey}"",
""RowKey"":""{rowKey}"",
""Timestamp"":""2001-02-03T16:05:06.007Z"",
""Status"":""Healthy"",
""Message"":""IGNORED""}}]}}"
                    )
                    {
                        Headers = {ContentType = MediaTypeHeaderValue.Parse("application/json")}
                    }
                }
            };

            var handler = new MockHandler(req =>
            {
                requestPaths.Add(req.RequestUri.AbsolutePath);
                return Task.FromResult(responses[requestPaths.Count - 1]());
            });
            
            await using ServiceProvider services = BuildServiceProvider(handler);
            var provider = services.GetRequiredService<IHealthReportProvider>();
            
            string serviceName = input + "-AS-SERVICE";
            string instanceName = input + "-AS-INSTANCE";
            string statusName = input + "-AS-STATUS";
            await provider.UpdateStatusAsync(serviceName, instanceName, statusName, HealthStatus.Healthy, "TEST STATUS MESSAGES");

            var ex = new Regex(@"\(\s*PartitionKey\s*=\s*'(.*)'\s*,\s*RowKey\s*=\s*'(.*)'\s*\)");
            requestPaths[0].Should().MatchRegex(ex);
            Match match = ex.Match( requestPaths[0]);
            partitionKey = Uri.UnescapeDataString(match.Groups[1].Value);
            rowKey = Uri.UnescapeDataString(match.Groups[2].Value);

            IList<HealthReport> status = await provider.GetAllStatusAsync(serviceName);
            status.Should().HaveCount(1);
            status[0].Service.Should().Be(serviceName);
            status[0].Instance.Should().Be(instanceName);
            status[0].SubStatus.Should().Be(statusName);
        }

        [Test]
        public async Task ReadAllHealthReports()
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
                        @"{""value"":[
{""PartitionKey"":""Microsoft.DotNet.Internal.Health.Tests.HealthReportingTests"",
""RowKey"":""TEST-INSTANCE|TEST-SUB-STATUS-INSTANCE"",
""Timestamp"":""2001-02-03T16:05:06.007Z"",
""Status"":""Healthy"",
""Message"":""TEST SUB-STATUS MESSAGES""},
{""PartitionKey"":""Microsoft.DotNet.Internal.Health.Tests.HealthReportingTests"",
""RowKey"":""|TEST-SUB-STATUS-SERVICE"",
""Timestamp"":""2001-02-03T17:05:06.007Z"",
""Status"":""Error"",
""Message"":""TEST STATUS MESSAGES""}
]}")
                    {
                        Headers = {ContentType = MediaTypeHeaderValue.Parse("application/json")}
                    }
                };
                return Task.FromResult(response);
            });

            await using ServiceProvider services = BuildServiceProvider(handler);
            var provider = services.GetRequiredService<IHealthReportProvider>();

            var report = await provider.GetAllStatusAsync(GetType().FullName);

            report.Should().HaveCount(2);

            {
                report.FirstOrDefault(r => r.Instance != null).Should().NotBeNull();
                var instanceReport = report.FirstOrDefault(r => r.Instance != null);

                instanceReport.Service.Should().Be(GetType().FullName);
                instanceReport.Instance.Should().Be("TEST-INSTANCE");
                instanceReport.Message.Should().Be("TEST SUB-STATUS MESSAGES");
                instanceReport.Health.Should().Be(HealthStatus.Healthy);
                instanceReport.SubStatus.Should().Be("TEST-SUB-STATUS-INSTANCE");
                instanceReport.AsOf.Should().Be(new DateTimeOffset(2001, 2, 3, 16, 5, 6, 7, TimeSpan.Zero));
            }

            {
                report.FirstOrDefault(r => r.Instance == null).Should().NotBeNull();
                var serviceReport = report.FirstOrDefault(r => r.Instance == null);

                serviceReport.Service.Should().Be(GetType().FullName);
                serviceReport.Message.Should().Be("TEST STATUS MESSAGES");
                serviceReport.Health.Should().Be(HealthStatus.Error);
                serviceReport.SubStatus.Should().Be("TEST-SUB-STATUS-SERVICE");
                serviceReport.AsOf.Should().Be(new DateTimeOffset(2001, 2, 3, 17, 5, 6, 7, TimeSpan.Zero));
            }
        }

        private static ServiceProvider BuildServiceProvider(MockHandler handler)
        {
            var instance = new Mock<IInstanceAccessor>();
            instance.Setup(i => i.GetCurrentInstanceName()).Returns(TestInstanceName);

            var collection = new ServiceCollection();
            collection.AddSingleton(instance.Object);
            collection.AddSingleton<IHttpClientFactory>(handler);
            collection.AddSingleton<IHttpMessageHandlerFactory>(handler);
            collection.AddHttpClient();
            collection.AddSingleton<ExponentialRetry>();
            collection.Configure<ExponentialRetryOptions>(o => o.RetryCount = 0);
            collection.AddHealthReporting(b => { b.AddAzureTable(TableUri.AbsoluteUri); });
            collection.AddLogging(b => b.AddProvider(new NUnitLogger()));

            return collection.BuildServiceProvider();
        }
    }
}
