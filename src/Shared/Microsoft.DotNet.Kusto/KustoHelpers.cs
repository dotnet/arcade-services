// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Ingest;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Kusto
{
    public static class KustoHelpers
    {
        public static async Task WriteDataToKustoInMemoryAsync<T>(
            IKustoIngestClient client,
            string databaseName,
            string tableName,
            ILogger logger,
            IEnumerable<T> data,
            Func<T, IList<KustoValue>> mapFunc)
        {
            CsvColumnMapping[] mappings = null;
            int size = 5;
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    foreach (T d in data)
                    {
                        var dataList = new List<string>(size);
                        if (mappings == null)
                        {
                            var mapList = new List<CsvColumnMapping>();
                            foreach (KustoValue p in mapFunc(d))
                            {
                                mapList.Add(new CsvColumnMapping {ColumnName = p.Column, CslDataType = p.DataType});
                                dataList.Add(p.StringValue);
                            }

                            mappings = mapList.ToArray();
                            size = mappings.Length;
                        }
                        else
                        {
                            dataList.AddRange(mapFunc(d).Select(p => p.StringValue));
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
    }
}
