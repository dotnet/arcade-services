// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class InitializeOperation : Operation
{
    private readonly InitializeCommandLineOptions _options;

    public InitializeOperation(InitializeCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    public override Task<int> ExecuteAsync()
    {
        throw new NotImplementedException();
    }
}
