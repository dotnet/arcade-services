using Microsoft.DncEng.CommandLineLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("pgp-key-pair")]
    internal class PgpKeyPair : SecretType<PgpKeyPair.Parameters>
    {
        private readonly IConsole _console;

        public class Parameters { }
    
        public PgpKeyPair(IConsole console)
        {
            _console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            // Adding this to suppress the warning about not having the 'await' operator in an async method
            await context.GetSecretValue(new SecretReference(context.SecretName));

            throw new HumanInterventionRequiredException($"Pgp key pair secret rotation required. Human intervention required. Please go to https://aka.ms/signalr-java-publishing and follow the instructions at the bottom of the page");
        }
    }
}
