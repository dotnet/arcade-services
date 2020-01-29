using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Kusto;

namespace Maestro.Web
{
    public static class Helpers
    {
        public static string GetApplicationVersion()
        {
            Assembly assembly = typeof(Helpers).Assembly;
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoVersion != null)
            {
                return infoVersion.InformationalVersion;
            }

            var version = assembly.GetCustomAttribute<AssemblyVersionAttribute>();
            if (version != null)
            {
                return version.Version;
            }

            return "42.42.42.42";
        }

        public static async Task<IActionResult> ProxyRequestAsync(this HttpContext context, HttpClient client, string targetUrl, Action<HttpRequestMessage> configureRequest)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, targetUrl))
            {
                foreach (var (key, values) in context.Request.Headers)
                {
                    switch (key.ToLower())
                    {
                        // We shouldn't copy any of these request headers
                        case "host":
                        case "authorization":
                        case "cookie":
                        case "content-length":
                        case "content-type":
                            continue;
                        default:
                            req.Headers.Add(key, values.ToArray());
                            break;
                    }
                }

                configureRequest(req);

                HttpResponseMessage res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                context.Response.RegisterForDispose(res);

                foreach (var (key, values) in res.Headers)
                {
                    switch (key.ToLower())
                    {
                        // Remove headers that the response doesn't need
                        case "set-cookie":
                        case "x-powered-by":
                        case "x-aspnet-version":
                        case "server":
                        case "transfer-encoding":
                        case "access-control-expose-headers":
                        case "access-control-allow-origin":
                            continue;
                        default:
                            if (!context.Response.Headers.ContainsKey(key))
                            {
                                context.Response.Headers.Add(key, values.ToArray());
                            }

                            break;
                    }
                }


                context.Response.StatusCode = (int) res.StatusCode;
                if (res.Content != null)
                {
                    foreach (var (key, values) in res.Content.Headers)
                    {
                        if (!context.Response.Headers.ContainsKey(key))
                        {
                            context.Response.Headers.Add(key, values.ToArray());
                        }
                    }

                    using (var data = await res.Content.ReadAsStreamAsync())
                    {
                        await data.CopyToAsync(context.Response.Body);
                    }
                }

                return new EmptyResult();
            }

        }

        public static Dictionary<string, KustoQuery> CreateBuildTimesQueries(string repository, string branch, int days)
        {
            var parameters = new List<KustoParameter> {
                new KustoParameter("_Repository", KustoDataTypes.String,  repository.Split("/").Last()),
                new KustoParameter("_SourceBranch", KustoDataTypes.String, branch),
                new KustoParameter("_Days", KustoDataTypes.TimeSpan, $"{days}d")
            };

            string publicProject = "public";

            // Builds in AzDo are only found in the internal project
            if (repository.Contains("dev.azure.com"))
            {
                publicProject = "internal";
            }

            // We only care about builds that complete successfully or partially successfully 
            // from the given repository. We summarize duration of the builds over the last specified
            // number of days. There are multiple different definitions that run in parallel, so we 
            // summarize on the definition id and ultimately choose the definition that took the longest.
            string commonQueryText = @"| where Result != 'failed' and Result != 'canceled' 
                | where FinishTime > ago(_Days) 
                | extend duration = FinishTime - StartTime 
                | summarize average_duration = avg(duration) by DefinitionId";

            // We only want the pull request time from the public ci. We exclude on target branch,
            // as all PRs come in as refs/heads/#/merge rather than what branch they are trying to
            // apply to.
            string publicQueryText = $@"TimelineBuilds 
                | project Repository, SourceBranch, TargetBranch, DefinitionId, StartTime, FinishTime, Result, Project, Reason
                | where Project == '{publicProject}'
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
                | where Reason == 'batchedCI' or Reason == 'individualCI'
                | where SourceBranch == _SourceBranch
                {commonQueryText}";

            Dictionary<string, KustoQuery> queries = new Dictionary<string, KustoQuery>();
            queries["internal"] = new KustoQuery(internalQueryText, parameters);
            queries["public"] = new KustoQuery(publicQueryText, parameters);

            return queries;
        }

        public static Tuple<int, TimeSpan> ParseBuildTime(IDataReader reader)
        {
            // There was an exception when we queried the database
            if (reader == null)
            {
                return null;
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
                return null;
            }

            int maxDefinitionId = buildTimeResults.Aggregate((l,r) => l.Value > r.Value ? l : r).Key;
            TimeSpan maxDuration = buildTimeResults[maxDefinitionId];

            return new Tuple<int, TimeSpan>(maxDefinitionId, maxDuration);
        }
    }
}
