using System.IO;
using System.Text;
using AwesomeAssertions;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests
{
    [TestFixture]
    public class MarkdownEncoderTests
    {
        private readonly MarkdownEncoder _markdownEncoder = new MarkdownEncoder();

        [Test]
        public void EncodeStringTest()
        {
            TextWriter target = new StringWriter();
            string text = "My<TestName>Is*String*Testing:#_";

            _markdownEncoder.Encode(text, target);
            target.ToString().Should().Be(@"My&#60;TestName&#62;Is&#42;String&#42;Testing:&#35;&#95;");
        }

        [Test]
        public void EncodeStringBuilderTest()
        {
            TextWriter target = new StringWriter();
            StringBuilder text = new StringBuilder("My<TestName>Is*StringBuilder*Testing:#_");

            _markdownEncoder.Encode(text, target);
            target.ToString().Should().Be(@"My&#60;TestName&#62;Is&#42;StringBuilder&#42;Testing:&#35;&#95;");
        }
    }
}
