// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.BarViz.Code.Services;

public class UserRoleManager
{
    private Lazy<bool> _isAdmin = new(async () =>
    {

    });

    public bool IsAdmin => _isAdmin.Value;

}
