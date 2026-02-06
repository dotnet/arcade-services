using System.ComponentModel;
using Newtonsoft.Json;

namespace Microsoft.Internal.Helix.KnownIssues.Models
{
    public class KnownIssueOptions
    {
        [DefaultValue(false)]
        public bool ExcludeConsoleLog { get; }

        [DefaultValue(false)]
        public bool RetryBuild { get; }

        [DefaultValue(false)]
        public bool RegexMatching { get; }

        public KnownIssueOptions(bool excludeConsoleLog = default, bool retryBuild = default, bool regexMatching = default)
        {
            ExcludeConsoleLog = excludeConsoleLog;
            RetryBuild = retryBuild;
            RegexMatching = regexMatching;
        }
    }
}
