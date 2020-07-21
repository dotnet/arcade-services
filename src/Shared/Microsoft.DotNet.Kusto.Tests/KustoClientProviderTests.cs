// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Options;
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

            using (var client = new KustoClientProvider(Options.Create(new KustoClientProviderOptions
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

            using (var client = new KustoClientProvider(Options.Create(new KustoClientProviderOptions
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

            using (var client = new KustoClientProvider(Options.Create(new KustoClientProviderOptions
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

        [Test]
        public async Task SemanticExceptionReturnsNull()
        {
            var queryProvider = new Mock<ICslQueryProvider>();
            queryProvider.Setup(q =>
                    q.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ClientRequestProperties>()))
                .Throws(new SemanticException());

            using (var client = new KustoClientProvider(Options.Create(new KustoClientProviderOptions
                    {QueryConnectionString = "IGNORED-CONNECTION-STRING", Database = "TEST-DATABASE",}),
                queryProvider.Object))
            {
                (await client.ExecuteKustoQueryAsync(new KustoQuery("basicQuery"))).Should().BeNull();
            }
        }
    }
}
