using System;
using System.Collections.Generic;
using System.Threading;
using Azure.Core;
using Microsoft.DncEng.Configuration.Extensions;
using Mono.Options;

namespace Microsoft.DncEng.Configuration.Bootstrap
{
    class Program
    {
        static void Main(string[] args)
        {
            var resources = new List<string>();
            var options = new OptionSet
            {
                {"r|resource=", "AAD resource identifiers to authenticate to", (string r) => resources.Add(r)},
            };
            options.Parse(args);
            Console.WriteLine("Bootstrapping configuration.");

            LocalDevTokenCredential.IsBoostrapping = true;
            var cred = new LocalDevTokenCredential();
            foreach (var resource in resources)
            {
                cred.GetToken(new TokenRequestContext(new[] {resource + "/.default"}), CancellationToken.None);
            }
        }
    }
}
