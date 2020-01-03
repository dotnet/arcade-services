using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace Microsoft.DotNet.Services.Utility
{
    public class AnnotationEntity : TableEntity
    {
        [IgnoreProperty]
        public string Service
        {
            get => PartitionKey;
            set => PartitionKey = value;
        }

        [IgnoreProperty]
        public string Id
        {
            get => RowKey;
            set => RowKey = value;
        }

        public int GrafanaAnnotationId { get; set; }
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? Ended { get; set; }

        public AnnotationEntity() : base()
        {
        }

        public AnnotationEntity(string service, string id) : base(service, id)
        {
        }

        public AnnotationEntity(string service, string id, int grafanaId) : base(service, id)
        {
            GrafanaAnnotationId = grafanaId;
            Started = DateTimeOffset.UtcNow;
        }
    }

    public class ScorecardEntity : TableEntity
    {
        public ScorecardEntity() : base()
        {
        }
        public ScorecardEntity(DateTimeOffset date, string repo) : base(date.ToString(FORMAT_CONSTANT), repo) { }

        private const string FORMAT_CONSTANT = "yyyy-MM-dd";

        [IgnoreProperty]
        public DateTimeOffset Date
        {
            get => DateTimeOffset.ParseExact(PartitionKey, FORMAT_CONSTANT, null);
            set => PartitionKey = value.ToString(FORMAT_CONSTANT);
        }
        [IgnoreProperty]
        public string Repo
        {
            get => RowKey;
            set => RowKey = value;
        }

        public int TotalScore { get; set; }
        public double TimeToRolloutSeconds { get; set; }
        public int CriticalIssues { get; set; }
        public int Hotfixes { get; set; }
        public int Rollbacks { get; set; }
        public double DowntimeSeconds { get; set; }
        public bool Failure { get; set; }
        public int TimeToRolloutScore { get; set; }
        public int CriticalIssuesScore { get; set; }
        public int HotfixScore { get; set; }
        public int RollbackScore { get; set; }
        public int DowntimeScore { get; set; }
    }
}
