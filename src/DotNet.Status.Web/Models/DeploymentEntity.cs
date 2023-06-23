using Azure;
using Azure.Data.Tables;
using System;
using System.Runtime.Serialization;

namespace DotNet.Status.Web.Models;

public class DeploymentEntity : ITableEntity
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    [IgnoreDataMember]
    public string Service => PartitionKey;
    [IgnoreDataMember]
    public string BuildNumber => RowKey;
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Ended { get; set; }
}
