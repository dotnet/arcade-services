using Microsoft.DotNet.Kusto;
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace Maestro.DataProviders
{
    public static class SharedKustoQueries
    {
        public static MultiProjectKustoQuery CreateBuildTimesQueries(
            string repository,
            string branch,
            int days,
            int? buildDefinitionId)
        {
            var parameters = new List<KustoParameter> {
                new KustoParameter("_Repository", repository.Split('/').Last(), KustoDataType.String),
                new KustoParameter("_SourceBranch", branch, KustoDataType.String),
                new KustoParameter("_Days", $"{days}d", KustoDataType.TimeSpan)
            };

            string prProject = "public";

            Uri uri = new Uri(repository);

            // Builds in AzDo are only found in the internal project
            if (uri.Host == "dev.azure.com")
            {
                prProject = "internal";
            }

            // We only care about builds that complete successfully or partially successfully 
            // from the given repository. We summarize duration of the builds over the last specified
            // number of days. There are multiple different definitions that run in parallel, so we 
            // summarize on the definition id and ultimately choose the definition that took the longest.
            string commonQueryText = @"| where Result != 'failed' and Result != 'canceled' 
                | where FinishTime > ago(_Days) 
                | extend duration = FinishTime - StartTime 
                | summarize average_duration = avg(duration) by DefinitionId";

            if (buildDefinitionId.HasValue)
            {
                // Build definition ID is stored in BAR as int
                // but in Kusto it's a string instead so we need
                // to convert it.
                parameters.Add(new KustoParameter(
                    "_BuildDefinitionId",
                    buildDefinitionId.ToString(),
                    KustoDataType.String));
                
                commonQueryText =
                    $@"| where DefinitionId == _BuildDefinitionId
                    {commonQueryText}";
            }

            // We only want the pull request time from the public ci. We exclude on target branch,
            // as all PRs come in as refs/heads/#/merge rather than what branch they are trying to
            // apply to.
            string publicQueryText = $@"TimelineBuilds 
                | project Repository, SourceBranch, TargetBranch, DefinitionId, StartTime, FinishTime, Result, Project, Reason
                | where Project == '{prProject}'
                | where Repository endswith _Repository
                | where Reason == 'pullRequest' 
                | where TargetBranch == _SourceBranch
                {commonQueryText}";

            // For the official build times, we want the builds that were generated as a CI run 
            // (either batchedCI or individualCI) for a specific branch--i.e. we want the builds
            // that are part of generating the product.
            string internalQueryText = $@"TimelineBuilds 
                | project Repository, SourceBranch, DefinitionId, StartTime, FinishTime, Result, Project, Reason
                | where Project == 'internal' 
                | where Repository endswith _Repository
                | where Reason == 'batchedCI' or Reason == 'individualCI' or Reason == 'manual'
                | where SourceBranch == _SourceBranch
                {commonQueryText}";

            return new MultiProjectKustoQuery(new KustoQuery(internalQueryText, parameters), new KustoQuery(publicQueryText, parameters));
        }

        public static (int buildId, TimeSpan buildTime) ParseBuildTime(IDataReader reader)
        {
            // There was an exception when we queried the database
            if (reader == null)
            {
                return (-1, default(TimeSpan));
            }
            Dictionary<int, TimeSpan> buildTimeResults = new Dictionary<int, TimeSpan>();

            while (reader.Read())
            {
                int definitionId = Int32.Parse(reader.GetString(0));
                TimeSpan duration = (TimeSpan) reader.GetValue(1);
                buildTimeResults[definitionId] = duration;
            }

            // There were no results
            if (buildTimeResults.Count() == 0)
            {
                return (-1, default(TimeSpan));
            }

            KeyValuePair<int, TimeSpan> maxPair = buildTimeResults.Aggregate((l,r) => l.Value > r.Value ? l : r);

            return (maxPair.Key, maxPair.Value);
        }
    }
}
