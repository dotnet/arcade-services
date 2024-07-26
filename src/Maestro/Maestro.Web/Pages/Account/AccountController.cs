// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maestro.Web.Pages.Account;

public class AccountController : Controller
{
    [HttpGet("/Account/SignOut")]
    [AllowAnonymous]
    public new async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/");
    }

    [HttpGet(AuthenticationConfiguration.AccountSignInRoute)]
    [AllowAnonymous]
    public IActionResult SignIn(string returnUrl = null)
    {
        return Challenge(new AuthenticationProperties() { RedirectUri = "/" }, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
