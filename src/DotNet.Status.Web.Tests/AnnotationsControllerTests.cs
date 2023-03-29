using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public class AnnotationsControllerTests
{
    [Test]
    public async Task StatusOkayTest()
    {
        // This endpoint is required by Grafana
        using TestData testData = new TestData();
        using HttpResponseMessage responseMessage = await testData.Client.GetAsync("/api/annotations");

        responseMessage.IsSuccessStatusCode.Should().BeTrue();
    }

    [Test]
    public async Task TooManyServicesRefusedTest()
    {
        using TestData testData = new TestData();

        // Do not process more than 10 elements in query
        string body = "" +
                      "{" +
                      "\"range\": {" +
                      "\"from\": \"2021-09-22T00:16:51.657Z\"," +
                      "\"to\": \"2021-09-29T00:16:51.657Z\"," +
                      "\"raw\": {" +
                      "\"from\": \"now-7d\"," +
                      "\"to\": \"now\"" +
                      "}" +
                      "}," +
                      "\"annotation\": {" +
                      "\"name\": \"New annotation\"," +
                      "\"datasource\": \"Rollout Annotations - Prod\"," +
                      "\"enable\": true," +
                      "\"iconColor\": \"red\"," +
                      "\"query\": \"s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11\"" +
                      "}," +
                      "\"rangeRaw\": {" +
                      "\"from\": \"now-7d\"," +
                      "\"to\": \"now\"" +
                      "}" +
                      "}";

        using StringContent stringContent = new StringContent(body)
        {
            Headers = {
                ContentType = new MediaTypeHeaderValue("application/json"),
                ContentLength = body.Length
            }
        };
        using HttpResponseMessage response = await testData.Client.PostAsync("/api/annotations/annotations", stringContent);

        // The query parses and returns anything
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Test]
    [Ignore("Not configured for CI; requires storage account or emulator")]
    public async Task QueryTest()
    {
        using TestData testData = new TestData();

        // Real traffic
        string body = "" +
                      "{" +
                      "\"range\": {" +
                      "\"from\": \"2021-09-22T00:16:51.657Z\"," +
                      "\"to\": \"2021-09-29T00:16:51.657Z\"," +
                      "\"raw\": {" +
                      "\"from\": \"now-7d\"," +
                      "\"to\": \"now\"" +
                      "}" +
                      "}," +
                      "\"annotation\": {" +
                      "\"name\": \"New annotation\"," +
                      "\"datasource\": \"Rollout Annotations - Prod\"," +
                      "\"enable\": true," +
                      "\"iconColor\": \"red\"," +
                      "\"query\": \"\"" +
                      "}," +
                      "\"rangeRaw\": {" +
                      "\"from\": \"now-7d\"," +
                      "\"to\": \"now\"" +
                      "}" +
                      "}";

        using StringContent stringContent = new StringContent(body) {
            Headers = {
                ContentType = new MediaTypeHeaderValue("application/json"),
                ContentLength = body.Length
            } 
        };
        using HttpResponseMessage response = await testData.Client.PostAsync("/api/annotations/annotations", stringContent);

        // The query parses and returns anything
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    public sealed class TestData : IDisposable
    {
        public HttpClient Client { get; }

        public TestData()
        {
            var factory = new TestAppFactory<DotNetStatusEmptyTestStartup>();

            factory.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(AnnotationsController).Assembly);

                services.Configure<GrafanaOptions>(options =>
                {
                    options.TableUri = "https://127.0.0.1:10002/devstoreaccount1/deployments";
                });

                services.AddLogging();
            });
            factory.ConfigureBuilder(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapControllers());
            });

            Client = factory.CreateClient();
        }

        public void Dispose()
        {
            Client?.Dispose();
        }
    }
}
