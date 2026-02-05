// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;
using ProductConstructionService.Cli.Options;

namespace ProductConstructionService.Cli.Operations;

internal class DeleteNamespaceOperation : IOperation
{
    private readonly IProductConstructionServiceApi _api;
    private readonly DeleteNamespaceOptions _options;

    public DeleteNamespaceOperation(IProductConstructionServiceApi api, DeleteNamespaceOptions options)
    {
        _api = api;
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        Console.WriteLine($"Deleting namespace '{_options.NamespaceName}' (SaveChanges: {_options.SaveChanges})...");

        var result = await _api.Ingestion.DeleteNamespaceAsync(
            _options.NamespaceName,
            _options.SaveChanges);

        if (result)
        {
            Console.WriteLine(_options.SaveChanges
                ? $"Namespace '{_options.NamespaceName}' was deleted successfully."
                : $"Namespace '{_options.NamespaceName}' would be deleted (dry run).");
            return 0;
        }
        else
        {
            Console.WriteLine($"Failed to delete namespace '{_options.NamespaceName}'.");
            return 1;
        }
    }
}
