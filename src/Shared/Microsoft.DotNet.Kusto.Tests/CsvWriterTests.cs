// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Kusto.Tests
{
    [TestFixture]
    public class CsvWriterTests
    {
        [Test]
        public async Task EmptyValue()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("");
            writer.ToString().Should().Be("\"\"\r\n");
        }

        [Test]
        public async Task NoFieldsThrows()
        {
            var writer = new StringWriter();
            await (((Func<Task>)(() => writer.WriteCsvLineAsync()))).Should().ThrowExactlyAsync<ArgumentException>();
        }

        [Test]
        public void ParseEmptyValue()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("\"\"\r\n");
            values.Should().ContainSingle();
            values[0].Should().ContainSingle();
            values[0][0].Should().Be("");
        }

        [Test]
        public void ParseEmptyFileNoNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("");
            values.Should().BeEmpty();
        }

        [Test]
        public void ParseEmptyFileWithNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("\n");
            values.Should().BeEmpty();
        }

        [Test]
        public void ParseEmptyFileWithCarriageReturnNewline()
        {
            var writer = new StringWriter();
            string[][] values = CsvWriterExtensions.ParseCsvFile("\r\n");
            values.Should().BeEmpty();
        }

        [Test]
        public async Task SimpleSingleValue()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a");
            writer.ToString().Should().Be("a\r\n");
        }

        [Test]
        public void ParseSimpleSingleValue()
        {
            var writer = new StringWriter();
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\r\n");
            values.Should().ContainSingle();
            values[0].Should().ContainSingle();
            values[0][0].Should().Be("a");
        }

        [Test]
        public async Task MultipleFields()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a", "1", "", "first");
            writer.ToString().Should().Be("a,1,,first\r\n");
        }

        [Test]
        public void ParseMultipleFields()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a,1,,first\r\n");
            values.Should().ContainSingle();
            values[0].Should().Equal(new[] {"a", "1", "", "first"});
        }

        [Test]
        public async Task QuotedField()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a", "a \"quoted\" string \r\n newlines", "first");
            writer.ToString().Should().Be("a,\"a \"\"quoted\"\" string \r\n newlines\",first\r\n");
        }

        [Test]
        public void ParseQuotedField()
        {
            string[][] values =
                CsvWriterExtensions.ParseCsvFile("a,\"a \"\"quoted\"\" string \r\n newlines\",first\r\n");
            values.Should().ContainSingle();
            values[0].Should().Equal(new[] {"a", "a \"quoted\" string \r\n newlines", "first"});
        }

        [Test]
        public void ParseMultipleSingleFieldRecordsNoNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\nb");
            values.Should().HaveCount(2);
            values[0].Should().Equal(new[] {"a"});
            values[1].Should().Equal(new[] {"b"});
        }

        [Test]
        public void ParseMultipleSingleFieldRecordsNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\nb\n");
            values.Should().HaveCount(2);
            values[0].Should().Equal(new[] {"a"});
            values[1].Should().Equal(new[] {"b"});
        }
    }
}
