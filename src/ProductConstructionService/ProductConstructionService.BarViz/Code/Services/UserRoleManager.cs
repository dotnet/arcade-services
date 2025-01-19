// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client;

namespace ProductConstructionService.BarViz.Code.Services;

public class UserRoleManager(IProductConstructionServiceApi pcsApi)
{
    private readonly Lazy<Task<bool>> _isAdmin = new(
        () => pcsApi.IsAdmin(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<bool> IsAdmin => _isAdmin.Value;
}
