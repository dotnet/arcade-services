using Azure.Data.Tables;
using DotNet.Status.Web.Models;
using DotNet.Status.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNet.Status.Web.Controllers;

/// <summary>
/// This Annotations controller serves queries in a format compatible with the Grafana
/// "Simple JSON Datasource". It is used to expose the "Deployments" table data to 
/// Grafana, which may then render the information in dashboards.
/// </summary>
[Route("api/annotations")]
[ApiController]
public class AnnotationsController : ControllerBase
{
    private const int _maximumServerCount = 10; // Safety limit on query complexity
    private readonly ILogger<AnnotationsController> _logger;
    private readonly IOptionsMonitor<GrafanaOptions> _options;

    public AnnotationsController(ILogger<AnnotationsController> logger, IOptionsMonitor<GrafanaOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    [HttpGet]
    public ActionResult Get()
    {
        return NoContent();
    }

    [HttpPost]
    [Route("query")]
    public ActionResult Query()
    {   // Required endpoint, but not used
        return NoContent();
    }

    [HttpPost]
    [Route("search")]
    public ActionResult Search()
    {   // Required endpoint, but not used
        return NoContent();
    }

    [HttpPost]
    [Route("annotations")]
    public async Task<ActionResult<IEnumerable<AnnotationEntry>>> Post(AnnotationQueryBody annotationQuery, CancellationToken cancellationToken)
    {
        IEnumerable<string> services = (annotationQuery.Annotation.Query?.Split(',') ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim());

        if (services.Count() > _maximumServerCount)
        {
            return BadRequest();
        }

        StringBuilder filterBuilder = new StringBuilder();
        filterBuilder.Append($"Started gt datetime'{annotationQuery.Range.From:O}' and Ended lt datetime'{annotationQuery.Range.To:O}'");
        if (services.Any())
        {
            filterBuilder.Append(" and (");
            filterBuilder.Append(string.Join(" or ", services.Select(s => $"PartitionKey eq '{s}'")));
            filterBuilder.Append(')');
        }

        string filter = filterBuilder.ToString();
        _logger.LogTrace("Compiled filter query: {Query}", filter);

        TableClient tableClient = new TableClient(new Uri(_options.CurrentValue.TableUri));
        IAsyncEnumerable<DeploymentEntity> entityQuery = tableClient.QueryAsync<DeploymentEntity>(
            filter: filter,
            cancellationToken: cancellationToken);

        List<AnnotationEntry> annotationEntries = new List<AnnotationEntry>();
        await foreach (DeploymentEntity entity in entityQuery)
        {
            AnnotationEntry entry;

            if (entity.Started != null && entity.Ended != null)
            {
                entry = new AnnotationEntry(
                    annotationQuery.Annotation,
                    entity.Started.Value.ToUnixTimeMilliseconds(),
                    $"Deployment of {entity.Service}")
                {
                    IsRange = true,
                    TimeEnd = entity.Ended.Value.ToUnixTimeMilliseconds()
                };
            }
            else if (entity.Started == null && entity.Ended == null)
            {
                continue;
            }
            else
            {
                entry = new AnnotationEntry(
                    annotationQuery.Annotation,
                    entity.Started?.ToUnixTimeMilliseconds() ?? entity.Ended.Value.ToUnixTimeMilliseconds(),
                    $"Deployment of {entity.Service}");
            }

            entry.Tags = new[] { "deploy", $"deploy-{entity.Service}", entity.Service };

            annotationEntries.Add(entry);
        }

        return annotationEntries;
    }
}
