using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class RepoConfig
    {
        public string Repo { get; set; }
        public List<string> BuildDefinitionIds { get; set; }
        public string AzdoInstance { get; set; }
        public string GithubIssueLabel { get; set; }
        public int ExpectedTime { get; set; }
        public List<string> ExcludeStages { get; set; }
    }
}
