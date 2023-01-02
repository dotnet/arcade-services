using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ScanOperation : Operation
{
    public ScanOperation(ScanCommandLineOptions options) : base(options, options.RegisterServices())
    {
    }

    public override async Task<int> ExecuteAsync()
    {
        var vmrScanner = Provider.GetRequiredService<IVmrScanner>();
        await vmrScanner.ScanVmr();
        return 0;
    }
}
