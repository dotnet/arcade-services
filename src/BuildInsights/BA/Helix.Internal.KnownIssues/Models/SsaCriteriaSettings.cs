using System.Collections.Generic;

namespace Microsoft.Internal.Helix.KnownIssues.Models;

public class SsaCriteriaSettings
{
    public int DailyHitsForEscalation { get; set; }
    public List<string> SsaRepositories { get; set; }
    public string SsaLabel { get; set; }
}
