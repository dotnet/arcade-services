// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ProductConstructionService.Api.Controllers;

/// <summary>
/// Enables us to create internal controllers so that they can have internal dependencies.
/// All controllers that inject internal dependencies should inherit from this class.
/// </summary>
public abstract class InternalController : Controller
{
}

public static class InternalControllersExtension
{
    public static IMvcBuilder EnableInternalControllers(this IMvcBuilder builder)
        => builder.ConfigureApplicationPartManager(manager =>
        {
            manager.FeatureProviders.Add(new CustomControllerFeatureProvider());
        });

    private class CustomControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo)
        {
            var isCustomController = !typeInfo.IsAbstract && typeof(InternalController).IsAssignableFrom(typeInfo);
            return isCustomController || base.IsController(typeInfo);
        }
    }
}
