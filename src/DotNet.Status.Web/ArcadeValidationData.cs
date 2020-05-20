using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotNet.Status.Web
{
    public class ArcadeValidationData
    {
        public DateTime BuildDateTime { get; set; }
        public string ArcadeVersion { get; set; }
        public int BARBuildID { get; set; }
        public string ArcadeBuildLink { get; set; }
        public string ArcadeValidationBuildLink { get; set; }
        public string ProductRepoName { get; set; }
        public string ProductRepoBuildLink { get; set; }
        public string ProductRepoBuildResult { get; set; }
        public string ArcadeDiffLink { get; set; }
    }
}
