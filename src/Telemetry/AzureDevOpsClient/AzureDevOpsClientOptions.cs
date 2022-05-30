using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.AzureDevOps
{
    public class AzureDevOpsClientOptions
    {
        public string BaseUrl { get; set; }
        public string Organization { get; set; }
        public int MaxParallelRequests { get; set; }
        public string AccessToken { get; set; }
    }
}
