using Kusto.Ingest;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public class NullKustoIngestClient : IKustoIngestClient
    {
        private static Task<IKustoIngestionResult> _successfulResultInstance = Task.FromResult<IKustoIngestionResult>(new AlwaysSuccessIngestionResult());

        private class AlwaysSuccessIngestionResult : IKustoIngestionResult
        {
            public IngestionStatus GetIngestionStatusBySourceId(Guid sourceId)
            {
                return new IngestionStatus() { Status = Status.Succeeded };
            }

            public IEnumerable<IngestionStatus> GetIngestionStatusCollection()
            {
                return new[] { new IngestionStatus() { Status = Status.Succeeded } };
            }
        }

        public void Dispose()
        {
            // Nothing to dispose
        }

        public Task<IKustoIngestionResult> IngestFromDataReaderAsync(IDataReader dataReader, KustoIngestionProperties ingestionProperties, DataReaderSourceOptions sourceOptions = null)
        {
            return _successfulResultInstance;
        }

        public Task<IKustoIngestionResult> IngestFromStorageAsync(string uri, KustoIngestionProperties ingestionProperties, StorageSourceOptions sourceOptions = null)
        {
            return _successfulResultInstance;
        }

        public Task<IKustoIngestionResult> IngestFromStreamAsync(Stream stream, KustoIngestionProperties ingestionProperties, StreamSourceOptions sourceOptions = null)
        {
            return _successfulResultInstance;
        }
    }
}
