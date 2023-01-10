using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo
{
    public class VmrCloakedFileScanner : VmrScanner
    {
        public VmrCloakedFileScanner(
            IVmrDependencyTracker dependencyTracker,
            IProcessManager processManager,
            IVmrInfo vmrInfo,
            ILogger<VmrScanner> logger)
            : base(dependencyTracker, processManager, vmrInfo, logger)
        {
        }

        protected override Task<string[]> ScanRepository(SourceMapping sourceMapping, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override string ScanType() => "cloaked";
    }
}
