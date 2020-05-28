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
<<<<<<< HEAD
<<<<<<< HEAD
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

        [HttpPost("collect/arcade-validation")]
<<<<<<< HEAD
        public async Task<IActionResult> CollectArcadeValidation([Required] ArcadeValidationData data)
        {
            TelemetryOptions options = _options.Value;

            if (_client == null && string.IsNullOrEmpty(options.KustoIngestConnectionString))
            {
                _logger.LogError("No KustoIngestConnectionString set");
                return StatusCode(500);
            }

            IKustoIngestClient ingest = _client ?? KustoIngestFactory.CreateQueuedIngestClient(options.KustoIngestConnectionString);
=======
=======
        private readonly IKustoIngestClient _client;
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web

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
=======
>>>>>>> Addressing minor code review feedback
        public async Task<IActionResult> CollectArcadeValidation([Required] ArcadeValidationData data)
        {
            TelemetryOptions options = _options.Value;

            if (_client == null && string.IsNullOrEmpty(options.KustoIngestConnectionString))
            {
                _logger.LogError("No KustoIngestConnectionString set");
                return StatusCode(500);
            }

<<<<<<< HEAD
            IKustoIngestClient ingest =
                KustoIngestFactory.CreateQueuedIngestClient(options.KustoIngestConnectionString);
>>>>>>> Initial commit for new API and test project
=======
            IKustoIngestClient ingest = _client ?? KustoIngestFactory.CreateQueuedIngestClient(options.KustoIngestConnectionString);
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web

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

<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
            return Ok();
=======
            return NoContent();
>>>>>>> Initial commit for new API and test project
=======
            _logger.LogInformation("End Collect Arcade Validation data");

=======
>>>>>>> Addressing minor code review feedback
            return Ok();
>>>>>>> Adding API for collecting telemetry from Arcade Validation runs; test project for DotNet.Status.Web
        }
    }
}
