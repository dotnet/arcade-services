// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Ingest;
using Microsoft.AspNetCore.Authentication;
using Microsoft.DotNet.Internal.Testing.Utility;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Kusto.Tests
{
    public class KustoHelpersTests
    {
        private readonly ITestOutputHelper _output;

        public KustoHelpersTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task EmptyWriteTestShouldNotSend()
        {
            var ingest = new Mock<IKustoIngestClient>();
            ingest.Setup(i => i.IngestFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<KustoIngestionProperties>(),
                    It.IsAny<StreamSourceOptions>()))
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()))
                .Verifiable();

            await KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                "TEST-DATABASE",
                "TEST-TABLE",
                new XUnitLogger(_output),
                Enumerable.Empty<int>(),
                null);

            ingest.VerifyNoOtherCalls();
        }

        [MemberData(nameof(GetBasicDataTypes))]
        [Theory]
        public async Task BasicSend(object inputValue, KustoDataType dataType, string representation)
        {
            string saved = null;
            var ingest = new Mock<IKustoIngestClient>();
            ingest.Setup(i => i.IngestFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<KustoIngestionProperties>(),
                    It.IsAny<StreamSourceOptions>()))
                .Callback((Stream s, KustoIngestionProperties p, StreamSourceOptions o) =>
                {
                    saved = new StreamReader(s).ReadToEnd();
                })
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));

            await KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                "TEST-DATABASE",
                "TEST-TABLE",
                new XUnitLogger(_output),
                new[] {inputValue},
                v => new[]
                {
                    new KustoValue("columnName", v, dataType),
                }
            );

            string[][] parsed = CsvWriterExtensions.ParseCsvFile(saved);
            Assert.Single(parsed);
            Assert.Single(parsed[0]);
            Assert.Equal(representation, parsed[0][0]);
        }

        [Fact]
        public async Task AssertPropertiesConfigured()
        {
            var ingest = new Mock<IKustoIngestClient>();
            KustoIngestionProperties props = null;
            ingest.Setup(i => i.IngestFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<KustoIngestionProperties>(),
                    It.IsAny<StreamSourceOptions>()))
                .Callback((Stream s, KustoIngestionProperties p, StreamSourceOptions o) => { props = p; })
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));

            await KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                "TEST-DATABASE",
                "TEST-TABLE",
                new XUnitLogger(_output),
                new[] {1},
                v => new[]
                {
                    new KustoValue("columnName", 1, KustoDataType.Int),
                }
            );

            Assert.NotNull(props);
            Assert.Equal("TEST-DATABASE", props.DatabaseName);
            Assert.Equal("TEST-TABLE", props.TableName);
        }

        [Fact]
        public async Task HandlesFieldsInAnyOrder()
        {
            var ingest = new Mock<IKustoIngestClient>();
            await Assert.ThrowsAsync<ArgumentException>(() => KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                    "TEST-DATABASE",
                    "TEST-TABLE",
                    new XUnitLogger(_output),
                    new[] {1, 2},
                    v => v switch
                    {
                        1 => new[]
                        {
                            new KustoValue("a", 1, KustoDataType.Int),
                            new KustoValue("b", "bValue1", KustoDataType.String),
                        },
                        2 => new[]
                        {
                            new KustoValue("b", "bValue2", KustoDataType.String),
                            new KustoValue("a", 2, KustoDataType.Int),
                        },
                        _ => throw new ArgumentOutOfRangeException()
                    }
                )
            );
        }

        [Fact]
        public async Task MultipleRecordsAndFieldsAreMapped()
        {
            string saved = null;
            KustoIngestionProperties props = null;
            var ingest = new Mock<IKustoIngestClient>();
            ingest.Setup(i => i.IngestFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<KustoIngestionProperties>(),
                    It.IsAny<StreamSourceOptions>()))
                .Callback((Stream s, KustoIngestionProperties p, StreamSourceOptions o) =>
                {
                    saved = new StreamReader(s).ReadToEnd();
                    props = p;
                })
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));
            await KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                    "TEST-DATABASE",
                    "TEST-TABLE",
                    new XUnitLogger(_output),
                    new[] {1, 2},
                    v => v switch
                    {

                        1 => new[]
                        {
                            new KustoValue("a", 1, KustoDataType.Int),
                            new KustoValue("b", "bValue1", KustoDataType.String),
                        },
                        2 => new[]
                        {
                            new KustoValue("a", 2, KustoDataType.Int),
                            new KustoValue("b", "bValue2", KustoDataType.String),
                        },
                        _ => throw new ArgumentOutOfRangeException()
                    }
                );
            
            Assert.NotNull(props.CSVMapping);
            Assert.Equal(2, props.CSVMapping.Count());
            CsvColumnMapping aMapping = props.CSVMapping.FirstOrDefault(m => m.ColumnName == "a");
            CsvColumnMapping bMapping = props.CSVMapping.FirstOrDefault(m => m.ColumnName == "b");
            Assert.NotNull(aMapping);
            Assert.NotNull(bMapping);
            Assert.NotEqual(aMapping.Ordinal, bMapping.Ordinal);
            Assert.Equal(KustoDataType.Int.CslDataType, aMapping.CslDataType);
            Assert.Equal(KustoDataType.String.CslDataType, bMapping.CslDataType);
            string[][] parsed = CsvWriterExtensions.ParseCsvFile(saved);
            Assert.Equal(2, parsed.Length);
            Assert.Equal(2, parsed[0].Length);
            Assert.Equal("1", parsed[0][aMapping.Ordinal]);
            Assert.Equal("bValue1", parsed[0][bMapping.Ordinal]);
            Assert.Equal("2", parsed[1][aMapping.Ordinal]);
            Assert.Equal("bValue2", parsed[1][bMapping.Ordinal]);
        }

        [Fact]
        public async Task NullRecordsAreSkipped()
        {
            string saved = null;
            KustoIngestionProperties props = null;
            var ingest = new Mock<IKustoIngestClient>();
            ingest.Setup(i => i.IngestFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<KustoIngestionProperties>(),
                    It.IsAny<StreamSourceOptions>()))
                .Callback((Stream s, KustoIngestionProperties p, StreamSourceOptions o) =>
                {
                    saved = new StreamReader(s).ReadToEnd();
                    props = p;
                })
                .Returns(Task.FromResult(Mock.Of<IKustoIngestionResult>()));
            await KustoHelpers.WriteDataToKustoInMemoryAsync(ingest.Object,
                "TEST-DATABASE",
                "TEST-TABLE",
                new XUnitLogger(_output),
                new[] {0, 1, 0, 2, 0},
                v => v switch
                {
                    0 => null,
                    1 => new[]
                    {
                        new KustoValue("a", 1, KustoDataType.Int),
                        new KustoValue("b", "bValue1", KustoDataType.String),
                    },
                    2 => new[]
                    {
                        new KustoValue("a", 2, KustoDataType.Int),
                        new KustoValue("b", "bValue2", KustoDataType.String),
                    },
                    _ => throw new ArgumentOutOfRangeException()
                }
            );
            
            Assert.NotNull(props.CSVMapping);
            Assert.Equal(2, props.CSVMapping.Count());
            CsvColumnMapping aMapping = props.CSVMapping.FirstOrDefault(m => m.ColumnName == "a");
            CsvColumnMapping bMapping = props.CSVMapping.FirstOrDefault(m => m.ColumnName == "b");
            Assert.NotNull(aMapping);
            Assert.NotNull(bMapping);
            Assert.NotEqual(aMapping.Ordinal, bMapping.Ordinal);
            Assert.Equal(KustoDataType.Int.CslDataType, aMapping.CslDataType);
            Assert.Equal(KustoDataType.String.CslDataType, bMapping.CslDataType);
            string[][] parsed = CsvWriterExtensions.ParseCsvFile(saved);
            Assert.Equal(2, parsed.Length);
            Assert.Equal(2, parsed[0].Length);
            Assert.Equal("1", parsed[0][aMapping.Ordinal]);
            Assert.Equal("bValue1", parsed[0][bMapping.Ordinal]);
            Assert.Equal("2", parsed[1][aMapping.Ordinal]);
            Assert.Equal("bValue2", parsed[1][bMapping.Ordinal]);
        }

        public static IEnumerable<object[]> GetBasicDataTypes()
        {
            var localTimeRepresentation = new DateTime(2001, 2, 3, 16, 5, 6, 7, DateTimeKind.Local).ToUniversalTime().ToString("O");
            return new[]
            {
                new object[] {null, KustoDataType.String, ""},
                new object[] {"aValue", KustoDataType.String, "aValue"},
                new object[] {2, KustoDataType.Int, "2"},
                new object[] {12345678901234567890L, KustoDataType.Long, "12345678901234567890"},
                new object[] {true, KustoDataType.Boolean, "True"},
                new object[] {new DateTime(2001, 2, 3, 16, 5, 6, 7, DateTimeKind.Local), KustoDataType.DateTime, localTimeRepresentation},
                new object[] {new DateTime(2001, 2, 3, 16, 5, 6, 7, DateTimeKind.Utc), KustoDataType.DateTime, "2001-02-03T16:05:06.0070000Z"},
                new object[] {new DateTimeOffset(2001, 2, 3, 16, 5, 6, 7, TimeSpan.Zero), KustoDataType.DateTime, "2001-02-03T16:05:06.0070000Z"},
                new object[] {new DateTimeOffset(2001, 2, 3, 16, 5, 6, 7, TimeSpan.FromHours(7)), KustoDataType.DateTime, "2001-02-03T09:05:06.0070000Z"},
                new object[] {3.5, KustoDataType.Real, "3.5"},
                new object[] {4.5f, KustoDataType.Real, "4.5"},
                new object[] {Guid.Parse("00000001-0002-0003-0004-000000000005"), KustoDataType.Guid, "00000001-0002-0003-0004-000000000005"},
                new object[] {new TimeSpan(1, 2, 3, 4, 5), KustoDataType.TimeSpan, "1.02:03:04.0050000"}
            };
        }
    }
}
