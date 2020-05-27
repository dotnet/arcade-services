using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Microsoft.DotNet.Kusto;
using Kusto.Ingest;
using System.Collections.Generic;

namespace DotNet.Status.Web.Controllers
{
    [Route("api/telemetry")]
    [ApiController]
    public class TelemetryController : ControllerBase
    {
        private readonly ILogger<TelemetryController> _logger;
        private readonly IOptionsSnapshot<TelemetryOptions> _options;
        private readonly IKustoIngestClient _client;

        public TelemetryController(
            ILogger<TelemetryController> logger,
            IOptionsSnapshot<TelemetryOptions> options,
            IKustoIngestClient client = null)
        {
            _logger = logger;
            _options = options;
            _client = client;
        }

        [HttpPost("collect/ArcadeValidation")]
        public async Task<IActionResult> CollectArcadeValidation([Required] ArcadeValidationData data)
        {
            _logger.LogInformation("Start Collect Arcade Validation data");

            TelemetryOptions options = _options.Value;

            if(null == options)
            {
                _logger.LogError("TelemetryOptions were not loaded.");
                return StatusCode(500);
            }

            if (null == _client && string.IsNullOrEmpty(options.KustoIngestConnectionString))
            {
                _logger.LogError("No KustoIngestConnectionString set");
                return StatusCode(500);
            }

            IKustoIngestClient ingest = _client ?? KustoIngestFactory.CreateQueuedIngestClient(options.KustoIngestConnectionString);

            List<ArcadeValidationData> arcadeValidationDatas = new List<ArcadeValidationData>{ data };

            await KustoHelpers.WriteDataToKustoInMemoryAsync(
                ingest,
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

            _logger.LogInformation("End Collect Arcade Validation data");

            return Ok();
        }
    }
}
