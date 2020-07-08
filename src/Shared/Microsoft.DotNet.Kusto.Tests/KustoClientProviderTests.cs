// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Capture = Moq.Capture;

namespace Microsoft.DotNet.Kusto.Tests
{
    public class KustoClientProviderTests
    {
        [Fact]
        public async Task ParameterlessQueryIsPassedPlainly()
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
                Assert.Same(result, reader);
            }

            Assert.Equal(new[] {"TEST-DATABASE"}, dbNames);
            Assert.Equal(new[] {"basicQuery"}, queries);
            Assert.Single(properties);
            Assert.Empty(properties[0].Parameters);
        }

        [Fact]
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
                IDataReader result = await client.ExecuteKustoQueryAsync(
                    new KustoQuery("basicQuery | where Id = _id and Name = _name",
                        new KustoParameter("_id", 9274, KustoDataType.Int),
                        new KustoParameter("_name", "TEST-NAME", KustoDataType.String)));
                Assert.Same(result, reader);
            }

            Assert.Equal(new[] {"TEST-DATABASE"}, dbNames);
            Assert.Single(queries);
            Assert.EndsWith("basicQuery | where Id = _id and Name = _name", queries[0]);
            var parameterDeclarationPattern = new Regex(@"declare\s*query_parameters\s*\(([^)]*)\)\s*;");
            Assert.Matches(parameterDeclarationPattern, queries[0]);
            string parametersString = parameterDeclarationPattern.Match(queries[0]).Groups[1].Value;
            IReadOnlyDictionary<string, string> parameters = parametersString.Split(',')
                .Select(p => p.Split(':', 2))
                .ToDictionary(p => p[0], p => p[1]);
            Assert.Equal(2, parameters.Count);
            Assert.Contains("_id", parameters);
            Assert.Equal(KustoDataType.Int.CslDataType, parameters["_id"]);
            Assert.Contains("_name", parameters);
            Assert.Equal(KustoDataType.String.CslDataType, parameters["_name"]);
            Assert.Single(properties);
            Assert.Equal(2, properties[0].Parameters.Count);
            Assert.Contains("_id", properties[0].Parameters);
            Assert.Equal("9274", properties[0].Parameters["_id"]);
            Assert.Contains("_name", properties[0].Parameters);
            Assert.Equal("TEST-NAME", properties[0].Parameters["_name"]);
        }

        [Fact]
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
                Assert.Null(await client.ExecuteKustoQueryAsync(new KustoQuery("basicQuery")));
            }
        }
    }
}
