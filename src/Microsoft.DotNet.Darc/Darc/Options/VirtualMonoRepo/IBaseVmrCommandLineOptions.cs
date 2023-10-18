// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

internal interface IBaseVmrCommandLineOptions : ICommandLineOptions
{
    string VmrPath { get; }

    string TmpPath { get; }

    IEnumerable<string> AdditionalRemotes { get; }

    IEnumerable<string> Repositories { get; }

    IServiceCollection RegisterServices();
}
