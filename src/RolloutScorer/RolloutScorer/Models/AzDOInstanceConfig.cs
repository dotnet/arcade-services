using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class AzdoInstanceConfig
    {
        public string Name { get; set; }
        public string Project { get; set; }
        public string PatSecretName { get; set; }
        public string KeyVaultUri { get; set; }
    }
}
