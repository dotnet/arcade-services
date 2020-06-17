using System;
using System.ComponentModel.DataAnnotations;

namespace DotNet.Status.Web
{
    public class ArcadeValidationData
    {
        [Required]
        public DateTime BuildDateTime { get; set; }
        [Required]
        public string ArcadeVersion { get; set; }
        [Required]
        public int BARBuildID { get; set; }
        [Required]
        public string ArcadeBuildLink { get; set; }
        [Required]
        public string ArcadeValidationBuildLink { get; set; }
        [Required]
        public string ProductRepoName { get; set; }
        [Required]
        public string ProductRepoBuildLink { get; set; }
        [Required]
        public string ProductRepoBuildResult { get; set; }
        [Required]
        public string ArcadeDiffLink { get; set; }
    }
}
