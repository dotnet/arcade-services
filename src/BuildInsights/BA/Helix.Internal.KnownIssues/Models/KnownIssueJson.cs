using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.KnownIssues.Models;

public class KnownIssueJson
{
    [JsonConverter(typeof(ErrorOrArrayOfErrorsConverter))]
    public List<string> ErrorMessage { get; set; }

    [JsonConverter(typeof(ErrorOrArrayOfErrorsConverter))]
    public List<string> ErrorPattern { get; set; }

    public bool BuildRetry { get; set; }

    [DefaultValue(false)]
    public bool ExcludeConsoleLog { get; set; }
}
