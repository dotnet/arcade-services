using Microsoft.DncEng.CommandLineLib;
using System;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    public static class ConsoleExtensions
    {
        const int DefaultRetries = 3;

        public static Task<string> AskUser(this IConsole console, string fieldName, string help, Func<string, bool> validation)
        {
            return AskUser(console, fieldName, help, validation, l => l);
        }

        public static async Task<T> AskUser<T>(this IConsole console, string fieldName, string help, Func<string, bool> validation, Func<string, T> parse)
        {
            int retries = DefaultRetries;

            while (retries-- > 0)
            {
                var field = await console.PromptAsync($"Enter {fieldName}: ");
                field = field?.Trim();
                if (validation(field))
                    return parse(field);

                console.WriteLine($"{fieldName} wasn't entered in the expected format. {help}");
            }

            throw new InvalidOperationException($"{fieldName} wasn't entered correctly in {DefaultRetries} attempts.");
        }
    }
}
