using System.IO;
using Xunit;

namespace Microsoft.DotNet.Kusto.Tests
{
    public class CsvWriterTests
    {
        [Fact]
        public void DelegateRequirementsMess_Pass()
        {
            StringWriter writer = new StringWriter();
            writer.WriteCsvLineAsync("a");
            Assert.Equal("a", writer.ToString());
        }
    }
}
