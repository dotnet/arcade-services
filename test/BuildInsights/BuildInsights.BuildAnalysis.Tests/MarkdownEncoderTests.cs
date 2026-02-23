// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using AwesomeAssertions;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests
{
    [TestFixture]
    public class MarkdownEncoderTests
    {
        private readonly MarkdownEncoder _markdownEncoder = new();

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
