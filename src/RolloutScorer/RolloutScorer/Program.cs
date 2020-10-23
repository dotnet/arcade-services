using Mono.Options;
using System.Net.Http.Headers;
using System.Reflection;

namespace RolloutScorer
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var commands = new CommandSet("RolloutScorer")
            {
                "usage: RolloutScorer COMMAND [OPTIONS]",
                "",
                "Available commands:",
                new ScoreCommand(), 
                new UploadCommand()
            };

            return commands.Run(args);
        }

        public static ProductInfoHeaderValue GetProductInfoHeaderValue()
        {
            return new ProductInfoHeaderValue(typeof(Program).Assembly.GetName().Name,
                typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
        }
    }
}
