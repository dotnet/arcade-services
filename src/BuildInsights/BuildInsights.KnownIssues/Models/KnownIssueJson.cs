// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BuildInsights.KnownIssues.Models;

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
