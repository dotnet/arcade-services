using System;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

namespace Microsoft.Dotnet.Grafana.Hooks
{
    public static class GithubIssueHook
    {
        public static readonly JsonSerializer s_serializer = new JsonSerializer();

        [FunctionName("GrafanaHookGithubCreate")]
        public static async Task<IActionResult> CreateIssue(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "grafana/github/create/{source:alpha}")] HttpRequest req,
            [Table("ActiveIssues")] CloudTable issuesTable,
            string source,
            ILogger log)
        {
            GrafanaAlertBody alert;
            using (var streamReader = new StreamReader(req.Body))
            using (JsonReader reader = new JsonTextReader(streamReader))
            {
                alert = s_serializer.Deserialize<GrafanaAlertBody>(reader);
            }

            switch (alert.State)
            {
                case "alerting":
                    await CreateNewAlertAsync(alert, issuesTable);
                    break;
                case "ok":
                    await ClearExistingAlertAsync(alert, issuesTable);
                    break;
            }

            return new NoContentResult();
        }

        private static async Task CreateNewAlertAsync(GrafanaAlertBody alert, string source, CloudTable issuesTable)
        {
            if (issuesTable.ExecuteAsync(TableOperation.Retrieve<>("source", alert.))
            throw new System.NotImplementedException();
        }
    }

    internal class ActiveAlertEntity : TableEntity
    {
        [IgnoreProperty]
        public string Source
        {
            get => PartitionKey;
            set => PartitionKey = value;
        }

        [IgnoreProperty]
        public string RuleUrl
        {
            get => RowKey;
            set => RowKey = value;
        }

        public string GitHubIssueId { get; set; }
    }

    public class GrafanaAlertBody
    {
        public GrafanaAlertBody(string title, int ruleId, string ruleName, string ruleUrl, string state, string imageUrl, string message, ImmutableArray<GrafanaAlertEvalMatches> evalMatches = default)
        {
            Title = title;
            RuleId = ruleId;
            RuleName = ruleName;
            RuleUrl = ruleUrl;
            State = state;
            ImageUrl = imageUrl;
            Message = message;
            EvalMatches = evalMatches;
        }

        public string Title { get; }
        public int RuleId { get; }
        public string RuleName { get; }
        public string RuleUrl { get; }
        public string State { get; }
        public string ImageUrl { get; }
        public string Message { get; }
        public ImmutableArray<GrafanaAlertEvalMatches> EvalMatches { get; }
    }

    public class GrafanaAlertEvalMatches
    {
        public GrafanaAlertEvalMatches(string metric, double value, ImmutableDictionary<string, string> tags = default)
        {
            Metric = metric;
            Value = value;
            Tags = tags;
        }

        public string Metric { get; }
        public ImmutableDictionary<string, string> Tags { get; }
        public double Value { get; }
    }
}
