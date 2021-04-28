using Microsoft.DncEng.CommandLineLib;
using System;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager
{
    internal static class ConsoleExtension
    {
        const int DefaultRetries = 3;

        public static Task<string> PromptAndValidateAsync(this IConsole console, string fieldName, string help, Func<string, bool> validation)
        {
            return PromptAndValidateAsync(console, fieldName, help, validation, l => l);
        }

        public static async Task<T> PromptAndValidateAsync<T>(this IConsole console, string fieldName, string help, Func<string, bool> validate, Func<string, T> parse)
        {
            int retries = DefaultRetries;

            while (retries-- > 0)
            {
                var field = await console.PromptAsync($"Enter {fieldName}: ");
                field = field?.Trim();
                if (validate(field))
                    return parse(field);

                console.WriteLine($"{fieldName} wasn't entered in the expected format. {help}");
            }

            throw new InvalidOperationException($"{fieldName} wasn't entered correctly in {DefaultRetries} attempts.");
        }
    }
}
