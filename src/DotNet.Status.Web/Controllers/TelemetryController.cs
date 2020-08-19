using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Microsoft.DotNet.Kusto;
using Kusto.Ingest;
using System.Collections.Generic;
using System;

namespace DotNet.Status.Web.Controllers
{
    [Route("api/telemetry")]
    [ApiController]
    public class TelemetryController : ControllerBase
    {
        private readonly ILogger<TelemetryController> _logger;
        private readonly IOptionsSnapshot<KustoOptions> _options;
        private readonly IKustoIngestClientFactory _clientFactory;

        public TelemetryController(
            ILogger<TelemetryController> logger,
            IOptionsSnapshot<KustoOptions> options,
            IKustoIngestClientFactory clientFactory)
        {
            _logger = logger;
            _options = options;
            _clientFactory = clientFactory;
        }

        [HttpPost("collect/arcade-validation")]
        public async Task<IActionResult> CollectArcadeValidation([Required] ArcadeValidationData data)
        {
            KustoOptions options = _options.Value;

            if (string.IsNullOrEmpty(options.IngestConnectionString))
            {
                throw new InvalidOperationException("No IngestConnectionString set");
            }

            List<ArcadeValidationData> arcadeValidationDatas = new List<ArcadeValidationData>{ data };

            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                _clientFactory.GetClient(),
                options.Database,
                "ArcadeValidation",
                _logger,
                arcadeValidationDatas,
                b => new[]
                {
                    new KustoValue("BuildDateTime", b.BuildDateTime, KustoDataType.DateTime),
                    new KustoValue("ArcadeVersion", b.ArcadeVersion, KustoDataType.String),
                    new KustoValue("BARBuildID", b.BARBuildID, KustoDataType.Int),
                    new KustoValue("ArcadeBuildLink", b.ArcadeBuildLink, KustoDataType.String),
                    new KustoValue("ArcadeValidationBuildLink", b.ArcadeValidationBuildLink, KustoDataType.String),
                    new KustoValue("ProductRepoName", b.ProductRepoName, KustoDataType.String),
                    new KustoValue("ProductRepoBuildLink", b.ProductRepoBuildLink, KustoDataType.String),
                    new KustoValue("ProductRepoBuildResult", b.ProductRepoBuildResult, KustoDataType.String),
                    new KustoValue("ArcadeDiffLink", b.ArcadeDiffLink, KustoDataType.String)
                });

            return Ok();

        }
    }
}
