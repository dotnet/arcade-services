using System;
using System.Text.RegularExpressions;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class RegexConfigMapper
    {
        public static Func<string, string> Create(Regex regex, Func<string, string> keyMapper)
        {
            return value =>
            {
                return regex.Replace(value, match =>
                {
                    string key = match.Groups["key"].Value;
                    return keyMapper(key);
                });
            };
        }
    }
}
