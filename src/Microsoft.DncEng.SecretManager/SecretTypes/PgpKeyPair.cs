using Microsoft.DncEng.CommandLineLib;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("pgp-key-pair")]
    public class PgpKeyPair : SecretType<PgpKeyPair.Parameters>
    {
        private readonly IConsole _console;

        public class Parameters { }

        public PgpKeyPair(IConsole console)
        {
            _console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            throw new HumanInterventionRequiredException("Pgp key pair secret rotation required. Human intervention required. Please go to https://aka.ms/signalr-java-publishing and follow the instructions at the bottom of the page");
        }
    }
}
