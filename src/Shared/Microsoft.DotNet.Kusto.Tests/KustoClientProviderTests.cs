// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Kusto.Cloud.Platform.Utils;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Results;
using Microsoft.DotNet.Internal.Testing.Utility;
using Moq;
using NUnit.Framework;

using Capture = Moq.Capture;

namespace Microsoft.DotNet.Kusto.Tests
{
    [TestFixture]
    public class KustoClientProviderTests
    {
        [Test]
        public async Task NoParameterQueryIsPassedPlainly()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            var dbNames = new List<string>();
            var queries = new List<string>();
            var properties = new List<ClientRequestProperties>();
            var reader = Mock.Of<IDataReader>();
            queryProvider.Setup(q =>
                    q.ExecuteQueryAsync(Capture.In(dbNames), Capture.In(queries), Capture.In(properties)))
                .Returns(Task.FromResult(reader));
            
            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
                           {QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE",}),
                       queryProvider.Object))
            {
                IDataReader result = await client.ExecuteKustoQueryAsync(new KustoQuery("basicQuery"));
                reader.Should().BeSameAs(result);
            }

            dbNames.Should().Equal(new[] {"TEST-DATABASE"});
            queries.Should().Equal(new[] {"basicQuery"});
            properties.Should().ContainSingle();
            properties[0].Parameters.Should().BeEmpty();
        }
        [Test]
        public async Task AssignedToTextPropertyIsPassedPlainly()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            var dbNames = new List<string>();
            var queries = new List<string>();
            var properties = new List<ClientRequestProperties>();
            var reader = Mock.Of<IDataReader>();
            queryProvider.Setup(q =>
                    q.ExecuteQueryAsync(Capture.In(dbNames), Capture.In(queries), Capture.In(properties)))
                .Returns(Task.FromResult(reader));

            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
                    {QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE",}),
                queryProvider.Object))
            {
                var query = new KustoQuery {Text = "basicQuery"};
                IDataReader result = await client.ExecuteKustoQueryAsync(query);
                reader.Should().BeSameAs(result);
            }

            dbNames.Should().Equal(new[] {"TEST-DATABASE"});
            queries.Should().Equal(new[] {"basicQuery"});
            properties.Should().ContainSingle();
            properties[0].Parameters.Should().BeEmpty();
        }

        [Test]
        public async Task ParameterizedQueryIncludesParameterInformation()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            var dbNames = new List<string>();
            var queries = new List<string>();
            var properties = new List<ClientRequestProperties>();
            var reader = Mock.Of<IDataReader>();
            queryProvider.Setup(q =>
                    q.ExecuteQueryAsync(Capture.In(dbNames), Capture.In(queries), Capture.In(properties)))
                .Returns(Task.FromResult(reader));

            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
                    {QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE",}),
                queryProvider.Object))
            {
                var query = new KustoQuery("basicQuery | where Id = _id and Name = _name",
                    new KustoParameter("_id", 9274, KustoDataType.Int));
                query.AddParameter("_name", "TEST-NAME", KustoDataType.String);
                IDataReader result = await client.ExecuteKustoQueryAsync(
                    query);
                reader.Should().BeSameAs(result);
            }

            dbNames.Should().Equal(new[] {"TEST-DATABASE"});
            queries.Should().ContainSingle();
            queries[0].Should().EndWith("basicQuery | where Id = _id and Name = _name");
            var parameterDeclarationPattern = new Regex(@"declare\s*query_parameters\s*\(([^)]*)\)\s*;");
            queries[0].Should().MatchRegex(parameterDeclarationPattern);
            string parametersString = parameterDeclarationPattern.Match(queries[0]).Groups[1].Value;
            IReadOnlyDictionary<string, string> parameters = parametersString.Split(',')
                .Select(p => p.Split(':', 2))
                .ToDictionary(p => p[0], p => p[1]);
            parameters.Should().HaveCount(2);
            parameters.Should().ContainKey("_id");
            parameters["_id"].Should().Be(KustoDataType.Int.CslDataType);
            parameters.Should().ContainKey("_name");
            parameters["_name"].Should().Be(KustoDataType.String.CslDataType);
            properties.Should().ContainSingle();
            properties[0].Parameters.Should().HaveCount(2);
            properties[0].Parameters.Should().ContainKey("_id");
            properties[0].Parameters["_id"].Should().Be("9274");
            properties[0].Parameters.Should().ContainKey("_name");
            properties[0].Parameters["_name"].Should().Be("TEST-NAME");
        }

        public class FakeSemanticException : SemanticException
        {
        }

        [Test]
        public async Task SemanticExceptionReturnsNull()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            queryProvider.Setup(q =>
                    q.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ClientRequestProperties>()))
                .Throws(new FakeSemanticException());

            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
                    {QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE",}),
                queryProvider.Object))
            {
                (await client.ExecuteKustoQueryAsync(new KustoQuery("basicQuery"))).Should().BeNull();
            }
        }

        [Test]
        public async Task ExecuteStreamableKustoQueryReturnsCorrectData()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            var dbNames = new List<string>();
            var queries = new List<string>();
            var properties = new List<ClientRequestProperties>();

            var dataReader = new Mock<IDataReader>();
            dataReader.Setup(m => m.FieldCount).Returns(2);
            dataReader.Setup(m => m.GetName(0)).Returns("puppies");
            dataReader.Setup(m => m.GetFieldType(0)).Returns(typeof(string));
            dataReader.SetupSequence(m => m.Read())
                .Returns(true)
                .Returns(false);
            MockDataTable mockDataTable = new MockDataTable(dataReader.Object);
          
            var returnDataSetFrames = new List<ProgressiveDataSetFrame>() 
            {
                new MockProgressiveDataSetFrame(new Dictionary<int, object[]>
                { 
                    {1, new object[] { "a", "b", 1, 2 } },
                    {2, new object[] { "c", "d", 3, 4 } }
                }),
                mockDataTable,
                new MockProgressiveDataSetTableCompletionFrame()

            };
            var returnDataSet = new ProgressiveDataSet(returnDataSetFrames.GetEnumerator());
            queryProvider.Setup(q =>
                    q.ExecuteQueryV2Async(Capture.In(dbNames), Capture.In(queries), Capture.In(properties)))
                .Returns(Task.FromResult(returnDataSet));

            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
            { QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE", }),
                queryProvider.Object))
            {
                var query = new KustoQuery("basicQuery | where Id = _id and Name = _name",
                    new KustoParameter("_id", 9274, KustoDataType.Int));
                query.AddParameter("_name", "TEST-NAME", KustoDataType.String);
                List<object[]> resultList = await client.ExecuteStreamableKustoQuery(query).ToListAsync();

                resultList.Should().NotBeEmpty()
                    .And.HaveCount(2)
                    .And.BeEquivalentTo(new object[] 
                        { 
                            new object[] { "a", "b", 1, 2 }, 
                            new object[] { "c", "d", 3, 4 }
                        });
            }

            dbNames.Should().Equal(new[] { "TEST-DATABASE" });
            queries.Should().ContainSingle();
            queries[0].Should().EndWith("basicQuery | where Id = _id and Name = _name");
            var parameterDeclarationPattern = new Regex(@"declare\s*query_parameters\s*\(([^)]*)\)\s*;");
            queries[0].Should().MatchRegex(parameterDeclarationPattern);
            string parametersString = parameterDeclarationPattern.Match(queries[0]).Groups[1].Value;
            IReadOnlyDictionary<string, string> parameters = parametersString.Split(',')
                .Select(p => p.Split(':', 2))
                .ToDictionary(p => p[0], p => p[1]);
            parameters.Should().HaveCount(2);
            parameters.Should().ContainKey("_id");
            parameters["_id"].Should().Be(KustoDataType.Int.CslDataType);
            parameters.Should().ContainKey("_name");
            parameters["_name"].Should().Be(KustoDataType.String.CslDataType);
            properties.Should().ContainSingle();
            properties[0].Parameters.Should().HaveCount(2);
            properties[0].Parameters.Should().ContainKey("_id");
            properties[0].Parameters["_id"].Should().Be("9274");
            properties[0].Parameters.Should().ContainKey("_name");
            properties[0].Parameters["_name"].Should().Be("TEST-NAME");
        }

        [Test]
        public void ExecuteStreamableKustoQueryThrowsWhenMultipleCompletionFramesReturned()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            var dbNames = new List<string>();
            var queries = new List<string>();
            var properties = new List<ClientRequestProperties>();

            var returnDataSetFrames = new List<ProgressiveDataSetFrame>()
            {
                new MockProgressiveDataSetTableCompletionFrame(),
                new MockProgressiveDataSetTableCompletionFrame()

            };
            var returnDataSet = new ProgressiveDataSet(returnDataSetFrames.GetEnumerator());
            queryProvider.Setup(q =>
                    q.ExecuteQueryV2Async(Capture.In(dbNames), Capture.In(queries), Capture.In(properties)))
                .Returns(Task.FromResult(returnDataSet));

            using (var client = new KustoClientProvider(MockOptionMonitor.Create(new KustoClientProviderOptions
            { QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE", }),
                queryProvider.Object))
            {
                var query = new KustoQuery("basicQuery | where Id = _id and Name = _name",
                    new KustoParameter("_id", 9274, KustoDataType.Int));
                query.AddParameter("_name", "TEST-NAME", KustoDataType.String);

                Func<Task> act = async () => await client.ExecuteStreamableKustoQuery(query).ToListAsync();
                act.Should().Throw<ArgumentException>();
            }
        }
    }

    public class MockProgressiveDataSetFrame : ProgressiveDataSetDataTableFragmentFrame
    {
        public MockProgressiveDataSetFrame(Dictionary<int, object[]> data)
        {
            _data.AddOrSetRange(data);
            _dataEnumerator = _data.GetEnumerator();
        }

        public FrameType FrameType => FrameType.TableFragment;

        public int TableId => throw new System.NotImplementedException();

        public int FieldCount => _data.First().Value.Length;

        public TableFragmentType FrameSubType => throw new System.NotImplementedException();

        public bool GetNextRecord(object[] values)
        {
            if (_dataEnumerator.MoveNext())
            {
                Array.Copy(_dataEnumerator.Current.Value, values, FieldCount);
                return true;
            }

            return false;
        }

        private static Dictionary<int, object[]> _data = new Dictionary<int, object[]>();

        private Dictionary<int, object[]>.Enumerator _dataEnumerator;
    }

    public class MockDataTable : DataTable, ProgressiveDataSetDataTableFrame
    {
        public MockDataTable(IDataReader tableData)
        {
            TableData = tableData;
        }

        public FrameType FrameType => FrameType.DataTable;
        public int TableId => 0;
        public WellKnownDataSet TableKind => WellKnownDataSet.QueryProperties;
        public IDataReader TableData { get; }
    }

    public class MockProgressiveDataSetTableCompletionFrame : ProgressiveDataSetTableCompletionFrame
    {
        public int TableId => throw new NotImplementedException();

        public long RowCount => throw new NotImplementedException();

        public FrameType FrameType => FrameType.TableCompletion;
    }
}
