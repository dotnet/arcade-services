// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.DotNet.Kusto.Tests
{
    public class CsvWriterTests
    {
        [Fact]
        public async Task EmptyValue()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("");
            Assert.Equal("\"\"\r\n", writer.ToString());
        }

        [Fact]
        public async Task NoFieldsThrows()
        {
            var writer = new StringWriter();
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteCsvLineAsync());
        }

        [Fact]
        public void ParseEmptyValue()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("\"\"\r\n");
            Assert.Single(values);
            Assert.Single(values[0]);
            Assert.Equal("", values[0][0]);
        }

        [Fact]
        public void ParseEmptyFileNoNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("");
            Assert.Empty(values);
        }

        [Fact]
        public void ParseEmptyFileWithNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("\n");
            Assert.Empty(values);
        }

        [Fact]
        public void ParseEmptyFileWithCarriageReturnNewline()
        {
            var writer = new StringWriter();
            string[][] values = CsvWriterExtensions.ParseCsvFile("\r\n");
            Assert.Empty(values);
        }

        [Fact]
        public async Task SimpleSingleValue()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a");
            Assert.Equal("a\r\n", writer.ToString());
        }

        [Fact]
        public void ParseSimpleSingleValue()
        {
            var writer = new StringWriter();
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\r\n");
            Assert.Single(values);
            Assert.Single(values[0]);
            Assert.Equal("a", values[0][0]);
        }

        [Fact]
        public async Task MultipleFields()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a", "1", "", "first");
            Assert.Equal("a,1,,first\r\n", writer.ToString());
        }

        [Fact]
        public void ParseMultipleFields()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a,1,,first\r\n");
            Assert.Single(values);
            Assert.Equal(new[] {"a", "1", "", "first"}, values[0]);
        }

        [Fact]
        public async Task QuotedField()
        {
            var writer = new StringWriter();
            await writer.WriteCsvLineAsync("a", "a \"quoted\" string \r\n newlines", "first");
            Assert.Equal("a,\"a \"\"quoted\"\" string \r\n newlines\",first\r\n", writer.ToString());
        }

        [Fact]
        public void ParseQuotedField()
        {
            string[][] values =
                CsvWriterExtensions.ParseCsvFile("a,\"a \"\"quoted\"\" string \r\n newlines\",first\r\n");
            Assert.Single(values);
            Assert.Equal(new[] {"a", "a \"quoted\" string \r\n newlines", "first"}, values[0]);
        }

        [Fact]
        public void ParseMultipleSingleFieldRecordsNoNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\nb");
            Assert.Equal(2, values.Length);
            Assert.Equal(new[] {"a"}, values[0]);
            Assert.Equal(new[] {"b"}, values[1]);
        }

        [Fact]
        public void ParseMultipleSingleFieldRecordsNewline()
        {
            string[][] values = CsvWriterExtensions.ParseCsvFile("a\nb\n");
            Assert.Equal(2, values.Length);
            Assert.Equal(new[] {"a"}, values[0]);
            Assert.Equal(new[] {"b"}, values[1]);
        }
    }
}
