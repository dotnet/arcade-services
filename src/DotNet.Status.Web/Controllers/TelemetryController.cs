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
        private readonly IKustoIngestClient _client;

        public TelemetryController(
            ILogger<TelemetryController> logger,
            IOptionsSnapshot<KustoOptions> options,
            IKustoIngestClient client)
        {
            _logger = logger;
            _options = options;
            _client = client;
        }

        [HttpPost("collect/arcade-validation")]
        public async Task<IActionResult> CollectArcadeValidation([Required] ArcadeValidationData data)
        {
            KustoOptions options = _options.Value;

            if (string.IsNullOrEmpty(options.KustoIngestConnectionString))
            {
                throw new InvalidOperationException("No KustoIngestConnectionString set");
            }

            List<ArcadeValidationData> arcadeValidationDatas = new List<ArcadeValidationData>{ data };

            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                _client,
                options.KustoDatabase,
                "ArcadeValidation",
                _logger,
                arcadeValidationDatas,
                b => new[]
                {
                    new KustoValue("BuildDateTime", b.BuildDateTime.ToString(), KustoDataTypes.DateTime),
                    new KustoValue("ArcadeVersion", b.ArcadeVersion, KustoDataTypes.String),
                    new KustoValue("BARBuildID", b.BARBuildID.ToString(), KustoDataTypes.Int),
                    new KustoValue("ArcadeBuildLink", b.ArcadeBuildLink, KustoDataTypes.String),
                    new KustoValue("ArcadeValidationBuildLink", b.ArcadeValidationBuildLink, KustoDataTypes.String),
                    new KustoValue("ProductRepoName", b.ProductRepoName, KustoDataTypes.String),
                    new KustoValue("ProductRepoBuildLink", b.ProductRepoBuildLink, KustoDataTypes.String),
                    new KustoValue("ProductRepoBuildResult", b.ProductRepoBuildResult, KustoDataTypes.String),
                    new KustoValue("ArcadeDiffLink", b.ArcadeDiffLink, KustoDataTypes.String)
                });

            return Ok();

        }
    }
}
