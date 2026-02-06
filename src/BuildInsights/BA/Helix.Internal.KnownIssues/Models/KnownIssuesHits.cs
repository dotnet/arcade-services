namespace Microsoft.Internal.Helix.KnownIssues.Models
{
    public class KnownIssuesHits
    {
        public int Daily { get; }
        public int Weekly { get; }
        public int Monthly { get; }

        public KnownIssuesHits(int daily, int weekly, int monthly)
        {
            Daily = daily;
            Weekly = weekly;
            Monthly = monthly;
        }
    }
}
