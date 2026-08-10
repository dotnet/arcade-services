// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace ProductConstructionService.Api.Configuration;

internal static class WebAuthenticationExtensions
{
    public static async Task<bool> IsAuthenticated(this HttpContext context)
    {
        var authTasks = AuthenticationConfiguration.AuthenticationSchemes.Select(context.AuthenticateAsync);
        var authResults = await Task.WhenAll(authTasks);
        var success = authResults.FirstOrDefault(result => result.Succeeded);
        if (success == null)
        {
            return false;
        }

        var authService = context.RequestServices.GetRequiredService<IAuthorizationService>();
        AuthorizationResult result = await authService.AuthorizeAsync(
            success.Ticket!.Principal,
            AuthenticationConfiguration.WebAuthorizationPolicyName);
        if (!result.Succeeded)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return false;
        }

        return true;
    }
}
