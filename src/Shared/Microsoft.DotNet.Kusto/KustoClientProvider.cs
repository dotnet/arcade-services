// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Net.Client;
using Kusto.Data.Results;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Kusto.Cloud.Platform.Data;

namespace Microsoft.DotNet.Kusto
{
    public sealed class KustoClientProvider : IKustoClientProvider, IDisposable
    {
        private readonly IOptionsMonitor<KustoClientProviderOptions> _options;
        private readonly object _updateLock = new object();
        private ICslQueryProvider _kustoQueryProvider;
        private readonly IDisposable _monitor;

        public KustoClientProvider(IOptionsMonitor<KustoClientProviderOptions> options)
        {
            _options = options;
            _monitor = options.OnChange(ClearProviderCache);
        }

        public KustoClientProvider(IOptionsMonitor<KustoClientProviderOptions> options, ICslQueryProvider provider)
        {
            _options = options;
            _kustoQueryProvider = provider;
        }

        private void ClearProviderCache(KustoClientProviderOptions arg1, string arg2)
        {
            lock (_updateLock)
            {
                _kustoQueryProvider = null;
            }
        }

        public ICslQueryProvider GetProvider()
        {
            var value = _kustoQueryProvider;
            if (value != null)
                return value;
            lock (_updateLock)
            {
                value = _kustoQueryProvider;
                if (value != null)
                    return value;

                _kustoQueryProvider = value = KustoClientFactory.CreateCslQueryProvider(_options.CurrentValue.QueryConnectionString);
                return value;
            }
        }

        private string DatabaseName => _options.CurrentValue.Database;

        public async Task<IDataReader> ExecuteKustoQueryAsync(KustoQuery query)
        {
            var client = GetProvider();
            var properties = BuildClientRequestProperties(query);

            string text = BuildQueryText(query);

            try
            {
                return await client.ExecuteQueryAsync(
                    DatabaseName,
                    text,
                    properties);
            }
            catch (SemanticException)
            {
                return null;
            }
        }

        /// <summary>
        /// Use this method to receive large quantities of data from Kusto in a "stream". 
        /// This method assumes the query is returning a single schema result set. 
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<object[]> ExecuteStreamableKustoQuery(KustoQuery query)
        {
            var client = GetProvider();
            var properties = BuildClientRequestProperties(query);
            properties.SetOption(ClientRequestProperties.OptionResultsProgressiveEnabled, true);

            string text = BuildQueryText(query);

            ProgressiveDataSet pDataSet = await client.ExecuteQueryV2Async(
                DatabaseName,
                text,
                properties);

            int tableCompletionCount = 0;

            using IEnumerator<ProgressiveDataSetFrame> frames = pDataSet.GetFrames();

            while (frames.MoveNext())
            {
                var frame = frames.Current;

                switch (frame.FrameType)
                {
                    case FrameType.TableFragment:
                    {
                        var content = frame as ProgressiveDataSetDataTableFragmentFrame;
                        while (GetNextRow(content, out object[] row))
                        {
                            yield return row;
                        }
                    }
                        break;

                    case FrameType.DataTable:
                    {
                        // Note from documentation: we can't skip processing the data -- we must consume it.
                        var content = frame as ProgressiveDataSetDataTableFrame;
                        var reader = content.TableData;
                        while (reader.Read())
                        {
                            var writer = new System.IO.StringWriter();
                            reader.WriteAsText("", true, writer,
                                firstOnly: false,
                                markdown: false,
                                includeWithHeader: "ColumnType",
                                includeHeader: true);
                        }
                    }
                        break;

                    case FrameType.TableCompletion:
                        tableCompletionCount++;
                        break;

                    case FrameType.DataSetHeader:
                    case FrameType.TableHeader:
                    case FrameType.TableProgress:
                    case FrameType.DataSetCompletion:
                    case FrameType.LastInvalid:
                    default:
                        break;
                }

                if (tableCompletionCount > 1)
                {
                    throw new ArgumentException("This method does not support multiple data result sets.");
                }
            }

            bool GetNextRow(ProgressiveDataSetDataTableFragmentFrame frame, out object[] row)
            {
                row = new object[frame.FieldCount];
                return frame.GetNextRecord(row);
            }
        }

        private ClientRequestProperties BuildClientRequestProperties(KustoQuery query)
        {
            var properties = new ClientRequestProperties();
            foreach (var parameter in query.Parameters)
            {
                properties.SetParameter(parameter.Name, parameter.Value.ToString());
            }

            return properties;
        }

        private string BuildQueryText(KustoQuery query)
        {
            string text = query.Text;
            if (query.Parameters?.Any() == true)
            {
                string parameterList = string.Join(",", query.Parameters.Select(p => $"{p.Name}:{p.Type.CslDataType}"));
                text = $"declare query_parameters ({parameterList});{query.Text}";
            }

            return text;
        }

        public void Dispose()
        {
            _kustoQueryProvider?.Dispose();
            _monitor?.Dispose();
        }
    }
}
