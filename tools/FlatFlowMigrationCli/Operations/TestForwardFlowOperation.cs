// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using FlatFlowMigrationCli.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli.Operations;

internal class TestForwardFlowOperation : IOperation
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly VmrDependencyResolver _vmrDependencyResolver;
    private readonly ILogger<TestForwardFlowOperation> _logger;
    private readonly TestForwardFowOptions _options;

    public TestForwardFlowOperation(
        IProductConstructionServiceApi pcsClient,
        VmrDependencyResolver vmrDependencyResolver,
        ILogger<TestForwardFlowOperation> logger,
        TestForwardFowOptions options)
    {
        _pcsClient = pcsClient;
        _vmrDependencyResolver = vmrDependencyResolver;
        _logger = logger;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        var vmrDependencies = await _vmrDependencyResolver.GetVmrDependencies(
            _options.VmrUri,
            "https://github.com/dotnet/sdk",
            "main");
        return 0;
    }
}

