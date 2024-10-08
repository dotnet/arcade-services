// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api.Controllers;

public class AccountController : Controller
{
    [HttpGet("/Account/SignOut")]
    [AllowAnonymous]
    public new async Task SignOut()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new()
        {
            RedirectUri = "/"
        });
    }

    [HttpGet(AuthenticationConfiguration.AccountSignInRoute)]
    [AllowAnonymous]
    public IActionResult SignIn(string? returnUrl = null)
    {
        returnUrl ??= "/";
        return Challenge(
            new AuthenticationProperties() { RedirectUri = returnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
