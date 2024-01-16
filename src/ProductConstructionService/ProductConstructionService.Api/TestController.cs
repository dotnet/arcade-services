// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api;
[Route("test")]
public class TestController(BuildAssetRegistryContext dbContext) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(dbContext.Subscriptions.Count());
    }
}
