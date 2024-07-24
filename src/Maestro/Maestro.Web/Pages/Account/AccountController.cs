// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.Authentication;
using Maestro.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Maestro.Web.Pages.Account;

public class AccountController : Controller
{
    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        BuildAssetRegistryContext context)
    {
        SignInManager = signInManager;
        UserManager = userManager;
        Context = context;
    }

    public SignInManager<ApplicationUser> SignInManager { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public BuildAssetRegistryContext Context { get; }

    [HttpGet("/Account/SignOut")]
    [AllowAnonymous]
    public new async Task<IActionResult> SignOut()
    {
        await SignInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }

    [HttpGet(AuthenticationConfiguration.AccountSignInRoute)]
    [AllowAnonymous]
    public IActionResult SignIn(string returnUrl = null)
    {
        string redirectUrl = Url.Action("login", "Account", new { returnUrl });
        AuthenticationProperties properties =
            SignInManager.ConfigureExternalAuthenticationProperties(OpenIdConnectDefaults.AuthenticationScheme, redirectUrl);
        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
