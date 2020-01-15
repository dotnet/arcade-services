using System;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.DotNet.Monitoring.Sdk;

namespace DotNet.Grafana.Tests
{
    public class MakePackTests
    {

        [Fact]
        public void ExtractDataSourceNamesTest()
        {
            //$.dashboard.panels[*]..datasource
            var dashboard = new JObject
            {
                {
                    "dashboard", new JObject
                    {
                        {
                            "panels",
                            new JArray
                            {
                                new JObject
                                {
                                    {"datasource", "Test Datasource 1"},
                                    {"other-property", "IGNORED"},
                                },
                                new JObject
                                {
                                    {"datasource", "Test Datasource 2"},
                                },
                            }
                        },
                        {"other-property", "IGNORED"},
                    }
                },
            };

            var expected = new List<string> {
                "Test Datasource 1",
                "Test Datasource 2",
            };

            IEnumerable<string> actual = GrafanaSerialization.ExtractDataSourceNames(dashboard);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SanitizeDataSourceTest()
        {
            var datasource = new JObject
            {
                {"id", "removed"},
                {"orgId", "removed"},
                {"url", "removed"},
                {"name", "datasource name"},
                {
                    "jsonData",
                    new JObject
                    {
                        {"safeData1", "value 1"},
                        {"safeData2", "value 2"},
                    }
                },
                {
                    "secureJsonFields",
                    new JObject
                    {
                        {"dangerousField1", "REMOVED"},
                        {"dangerousField2", "REMOVED"},
                    }
                },
            };

            var result = GrafanaSerialization.SanitizeDataSource(datasource);

            // These are instance dependent, so need to be stripped
            Assert.Null(result["id"]);
            Assert.Null(result["orgId"]);
            Assert.Null(result["url"]);

            // These are secure, so they shouldn't be exported
            string df1 = result.Value<JObject>("secureJsonFields")?.Value<string>("dangerousField1");
            Assert.StartsWith("[vault(", df1);
            Assert.Contains("dangerousField1", df1);
            Assert.DoesNotContain("REMOVED", df1);

            string df2 = result.Value<JObject>("secureJsonFields")?.Value<string>("dangerousField2");
            Assert.StartsWith("[vault(", df2);
            Assert.Contains("dangerousField2", df2);
            Assert.DoesNotContain("REMOVED", df2);

            // This is safe, so it should be preserved
            Assert.Equal("value 1", result.Value<JObject>("jsonData")?.Value<string>("safeData1"));
        }
    }
}
