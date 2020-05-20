using System;
using System.Collections.Generic;
<<<<<<< HEAD
using System.ComponentModel.DataAnnotations;
=======
>>>>>>> Initial commit for new API and test project
using System.Linq;
using System.Threading.Tasks;

namespace DotNet.Status.Web
{
    public class ArcadeValidationData
    {
<<<<<<< HEAD
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
=======
        public DateTime BuildDateTime { get; set; }
        public string ArcadeVersion { get; set; }
        public int BARBuildID { get; set; }
        public string ArcadeBuildLink { get; set; }
        public string ArcadeValidationBuildLink { get; set; }
        public string ProductRepoName { get; set; }
        public string ProductRepoBuildLink { get; set; }
        public string ProductRepoBuildResult { get; set; }
>>>>>>> Initial commit for new API and test project
        public string ArcadeDiffLink { get; set; }
    }
}
