// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine.Text;
using CommandLine;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class PushOperation : Operation
{
    public PushOperation(VmrPushCommandLineOptions options)
        : base(options)
    {
    }

    public override Task<int> ExecuteAsync()
    {
        throw new NotImplementedException();
    }
}
