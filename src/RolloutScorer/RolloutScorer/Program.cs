using Mono.Options;

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
                new UploadCommand(),
            };

            return commands.Run(args);
        }
    }
}
