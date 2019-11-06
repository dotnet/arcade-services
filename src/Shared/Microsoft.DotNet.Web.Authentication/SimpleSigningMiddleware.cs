// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Policy;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Web.Authentication
{
    public interface IUserFactory<TUser> where TUser : class
    {
        Task<TUser> CreateAsync(ExternalLoginInfo info);
    }

    public class SimpleSigninOptions
    {
        public string ChallengeScheme { get; set; }
        public string AuthCallbackUrl { get; set; } = "/account/signin";
    }

    public class EmptyUser
    {
    }

    public class SimpleSigningMiddleware : IMiddleware
    {
        private readonly IOptions<SimpleSigninOptions> _options;
        private readonly CookieAuthenticationOptions _cookieOptions;

        public SimpleSigningMiddleware(
            IOptions<SimpleSigninOptions> options,
            IOptionsSnapshot<CookieAuthenticationOptions> cookieOptions)
        {
            _options = options;
            _cookieOptions = cookieOptions.Get(IdentityConstants.ApplicationScheme);
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Path.Equals(_cookieOptions.LoginPath))
            {
                await SignIn(context);
            }
            else if (context.Request.Path.Equals(_options.Value.AuthCallbackUrl))
            {
                await AuthCallback(context);
            }
            else if (context.Request.Path.Equals(_cookieOptions.LogoutPath))
            {
                await SignOut(context);
                context.Response.Redirect("/");
            }
            else
            {
                await next(context);
            }
        }

        private async Task SignOut(HttpContext context)
        {
            await context.SignOutAsync(_options.Value.ChallengeScheme);
        }

        private async Task SignIn(HttpContext context)
        {
            string query = "";
            if (context.Request.Query.TryGetValue("r", out var returnValues))
            {
                query = "?r=" + Uri.EscapeDataString(returnValues.ToString());
            }

            SimpleSigninOptions options = _options.Value;
            string redirectUrl = new Uri(new Uri(context.Request.GetEncodedUrl()), $"{_options.Value.AuthCallbackUrl}{query}").AbsoluteUri;
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            await context.ChallengeAsync(options.ChallengeScheme, properties);
        }

        private async Task AuthCallback(HttpContext context)
        {
            if (context.Request.Query.TryGetValue("remoteError", out var remoteError))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(remoteError.ToString());
                return;
            }

            await context.SignInAsync(IdentityConstants.ApplicationScheme, context.User);

            string returnUrl = "/";
            if (context.Request.Query.TryGetValue("r", out var returnUrlParams))
            {
                returnUrl = returnUrlParams.ToString();
            }
            
            context.Response.Redirect(returnUrl);
        }
    }
}
