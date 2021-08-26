// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Kusto
{
    public static class KustoHelpers
    {
        // we can't use "ToAsyncEnumerable" because of the namespace that's in and name conflicts in EF core
        // https://github.com/dotnet/efcore/issues/18124
        private class AsyncEnumerableWrapper<T> : IAsyncEnumerable<T>
        {
            private readonly IEnumerable<T> _inner;

            public AsyncEnumerableWrapper(IEnumerable<T> inner)
            {
                _inner = inner;
            }

            public class Enumerator : IAsyncEnumerator<T>
            {
                private readonly IEnumerator<T> _inner;

                public Enumerator(IEnumerator<T> inner)
                {
                    _inner = inner;
                }

                public ValueTask DisposeAsync()
                {
                    _inner.Dispose();
                    return default;
                }

                public ValueTask<bool> MoveNextAsync()
                {
                    return new ValueTask<bool>(_inner.MoveNext());
                }

                public T Current => _inner.Current;
            }
            
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
            {
                return new Enumerator(_inner.GetEnumerator());
            }
        }

        public static Task WriteDataToKustoInMemoryAsync<T>(
            IKustoIngestClient client,
            string databaseName,
            string tableName,
            ILogger logger,
            IEnumerable<T> data,
            Func<T, IList<KustoValue>> mapFunc) =>
            WriteDataToKustoInMemoryAsync(client, databaseName, tableName, logger, new AsyncEnumerableWrapper<T>(data), mapFunc);

        public static async Task WriteDataToKustoInMemoryAsync<T>(
            IKustoIngestClient client,
            string databaseName,
            string tableName,
            ILogger logger,
            IAsyncEnumerable<T> data,
            Func<T, IList<KustoValue>> mapFunc)
        {
            CsvColumnMapping[] mappings = null;
            int size = 5;
            await using var stream = new MemoryStream();
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
            {
                await foreach (T d in data)
                {
                    IList<KustoValue> kustoValues = mapFunc(d);
                    if (kustoValues == null)
                    {
                        continue;
                    }

                    var dataList = new List<string>(size);
                    if (mappings == null)
                    {
                        var mapList = new List<CsvColumnMapping>();
                        foreach (KustoValue p in kustoValues)
                        {
                            mapList.Add(new CsvColumnMapping {ColumnName = p.Column, CslDataType = p.DataType.CslDataType});
                            dataList.Add(p.StringValue);
                        }

                        mappings = mapList.ToArray();
                        size = mappings.Length;
                    }
                    else
                    {
                        if (!kustoValues.Select(v => v.Column).SequenceEqual(mappings.Select(m => m.ColumnName)))
                        {
                            throw new ArgumentException("Fields must be supplied in the same order for each record");
                        }

                        dataList.AddRange(kustoValues.Select(p => p.StringValue));
                    }

                    await writer.WriteCsvLineAsync(dataList);
                }
            }

            if (mappings == null)
            {
                logger.LogInformation("No rows to upload.");
                return;
            }

            for (int i = 0; i < mappings.Length; i++)
            {
                mappings[i].Ordinal = i;
            }

            stream.Seek(0, SeekOrigin.Begin);

            logger.LogInformation($"Ingesting {mappings.Length} columns at {stream.Length} bytes...");

            await client.IngestFromStreamAsync(
                stream,
                new KustoQueuedIngestionProperties(databaseName, tableName)
                {
                    Format = DataSourceFormat.csv,
                    ReportLevel = IngestionReportLevel.FailuresOnly,
                    ReportMethod = IngestionReportMethod.Queue,
                    CSVMapping = mappings
                });

            logger.LogTrace("Ingest complete");
        }
    }

    public class KustoIngestClientFactory : IKustoIngestClientFactory
    {
        private readonly IOptionsMonitor<KustoOptions> _kustoOptions;
        private readonly ConcurrentDictionary<string, IKustoIngestClient> _clients = new ConcurrentDictionary<string, IKustoIngestClient>();

        public KustoIngestClientFactory(IOptionsMonitor<KustoOptions> options)
        {
            _kustoOptions = options;
        }

        public IKustoIngestClient GetClient()
        {
            string ingestConnectionString = _kustoOptions.CurrentValue.IngestConnectionString;

            if (string.IsNullOrWhiteSpace(ingestConnectionString))
                throw new InvalidOperationException($"Kusto {nameof(_kustoOptions.CurrentValue.IngestConnectionString)} is not configured in settings or related KeyVault");

            return _clients.GetOrAdd(ingestConnectionString, _ => KustoIngestFactory.CreateQueuedIngestClient(ingestConnectionString));
        }
    }

    public interface IKustoIngestClientFactory
    {
        IKustoIngestClient GetClient();
    }

    public class KustoOptions
    {
        public string QueryConnectionString { get; set; }
        public string IngestConnectionString { get; set; }
        public string Database { get; set; }
    }
}
