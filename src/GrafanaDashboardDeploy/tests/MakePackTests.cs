using System;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Collections.Generic;

namespace DotNet.Grafana.Tests
{
    public class MakePackTests
    {

        [Fact]
        public void ExtractDataSourceNamesTest()
        {
            string testFilePath = Path.Join(AppContext.BaseDirectory, "resources", "dashboard 83dI-D2Zz.json");
            using var sr = new StreamReader(testFilePath);
            using var jr = new JsonTextReader(sr);
                
            var dashboard = JObject.Load(jr);
            var expected = new List<string> {
                "Kusto - engdata (Staging)",
                "AI - helix-autoscale-int"
            };

            var actual = Util.ExtractDataSourceNames(dashboard);

            Assert.Equal<string>(expected, actual);
        }

        [Fact]
        public void SanatizeDataSourceTest()
        {
            string testFilePath = Path.Join(AppContext.BaseDirectory, "resources", "datasource AI - helix-autoscale-int.json");
            using var sr = new StreamReader(testFilePath);
            using var jr = new JsonTextReader(sr);

            var datasource = JObject.LoadAsync(jr).Result;
            var result = Util.SanitizeDataSource(datasource);

            // These must be removed
            Assert.Null(result["id"]);
            Assert.Null(result["orgId"]);
            Assert.Null(result["url"]);

            // These values are added as placeholders based on other data in the document
            Assert.Equal("[vault(appInsightsApiKey)]", result["secureJsonData"]["appInsightsApiKey"]);
            Assert.Equal("[vault(clientSecret)]", result["secureJsonData"]["clientSecret"]);
        }

        [Fact]
        public void SanatizeFolderTest()
        {
            string testFilePath = Path.Join(AppContext.BaseDirectory, "resources", "folder 10 xxmzSkJZk.json");
            using var sr = new StreamReader(testFilePath);
            using var jr = new JsonTextReader(sr);

            var folder = JObject.Load(jr);

            var result = Util.SanitizeFolder(folder);

            Assert.Equal("xxmzSkJZk", result["uid"]);
            Assert.Equal("Engineering Staging", result["title"]);
        }
    }
}
