using Azure;
using Azure.Data.Tables;
using System;

namespace DotNet.Status.Web.Models
{
    public class DeploymentEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Service => PartitionKey;
        public string BuildNumber => RowKey;
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? Ended { get; set; }
    }
}
