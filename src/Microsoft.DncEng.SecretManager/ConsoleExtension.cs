using Microsoft.DncEng.CommandLineLib;
using System;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager;

internal static class ConsoleExtension
{
    const int DefaultRetries = 3;

    public delegate bool TryParse<TParsed>(string value, out TParsed parsedValue);

    public static Task<string> PromptAndValidateAsync(this IConsole console, string fieldName, string help, Func<string, bool> validate)
    {
        return PromptAndValidateAsync(console, fieldName, help,
            (string value, out string parsedValue) => { parsedValue = value; return validate(value); });
    }

    public static async Task<TParsed> PromptAndValidateAsync<TParsed>(this IConsole console, string fieldName, string help, TryParse<TParsed> tryParse)
    {
        int retries = DefaultRetries;

        while (retries-- > 0)
        {
            var field = await console.PromptAsync($"Enter {fieldName}: ");
            field = field?.Trim();
            if (tryParse(field, out TParsed parsedField))
                return parsedField;

            console.WriteLine($"{fieldName} wasn't entered in the expected format. {help}");
        }

        throw new InvalidOperationException($"{fieldName} wasn't entered correctly in {DefaultRetries} attempts.");
    }
}
