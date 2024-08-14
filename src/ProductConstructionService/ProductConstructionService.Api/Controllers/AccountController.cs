// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProductConstructionService.Api.Controllers;

public class AccountController : Controller
{
    [HttpGet("/Account/SignOut")]
    [AllowAnonymous]
    public new async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync();
        return Redirect($"{Request.Scheme}://{Request.Host}");
    }

    [HttpGet(AuthenticationConfiguration.AccountSignInRoute)]
    [AllowAnonymous]
    public IActionResult SignIn()
    {
        return Challenge(
            new AuthenticationProperties() { RedirectUri = $"{Request.Scheme}://{Request.Host}" },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
