using System;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace DotNet.Grafana.Tests
{
    public class DeployToolTests
    {
        [Theory]
        [InlineData("[Vault(secretName)]", "secretName")]
        [InlineData("[vault(secretName)]", "secretName")]
        [InlineData("[vault(secret name)]", "secret name")]
        [InlineData("[vault(secret ☃)]", "secret ☃")]
        public void SuccessfulTryGetSecretNameTest(string data, string secret)
        {
            string actual;

            Assert.True(DeployTool.TryGetSecretName(data, out actual));
            Assert.Equal(secret, actual);
        }


        [Theory]
        [InlineData("vault(secretName)")]
        [InlineData("(vault[secretName])")]
        public void FailingTryGetSecretNameTest(string data)
        {
            string actual;

            Assert.False(DeployTool.TryGetSecretName(data, out actual));
            Assert.Equal(String.Empty, actual);
        }

        [Fact]
        public void TestExtractFolderId()
        {
            string testFilePath = Path.Join(AppContext.BaseDirectory, "resources", "dashboard 83dI-D2Zz.json");
            using var sr = new StreamReader(testFilePath);
            using var jr = new JsonTextReader(sr);
            var dashboard = JObject.LoadAsync(jr).Result;

            var expected = 10;

            var actual = Util.ExtractFolderId(dashboard);

            Assert.Equal(expected, actual);
        }
    }
}
