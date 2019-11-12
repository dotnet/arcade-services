// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Web.Authentication
{
    public class ContextAwareAuthenticationSchemeProvider : AuthenticationSchemeProvider
    {
        private readonly IOptions<ContextAwareAuthenticationSchemeOptions> _contextOptions;
        private readonly IHttpContextAccessor _contextAccessor;

        public ContextAwareAuthenticationSchemeProvider(
            IOptions<ContextAwareAuthenticationSchemeOptions> contextOptions,
            IOptions<AuthenticationOptions> options,
            IHttpContextAccessor contextAccessor) : base(options)
        {
            _contextOptions = contextOptions;
            _contextAccessor = contextAccessor;
        }

        public HttpContext Context => _contextAccessor.HttpContext;

        private Task<AuthenticationScheme> GetDefaultSchemeAsync()
        {
            string DefaultResolve(PathString p)
            {
                if (p.StartsWithSegments("/api"))
                {
                    return PersonalAccessTokenDefaults.AuthenticationScheme;
                }

                return IdentityConstants.ApplicationScheme;
            }

            Func<PathString, string> func = _contextOptions?.Value?.SelectScheme ?? DefaultResolve;

            return GetSchemeAsync(func(Context.Request.Path));
        }

        public override Task<AuthenticationScheme> GetDefaultAuthenticateSchemeAsync()
        {
            return GetDefaultSchemeAsync();
        }

        public override Task<AuthenticationScheme> GetDefaultChallengeSchemeAsync()
        {
            return GetSchemeAsync(IdentityConstants.ExternalScheme);
        }

        public override Task<AuthenticationScheme> GetDefaultForbidSchemeAsync()
        {
            return GetDefaultSchemeAsync();
        }

        public override Task<AuthenticationScheme> GetDefaultSignInSchemeAsync()
        {
            return GetSchemeAsync(IdentityConstants.ApplicationScheme);
        }

        public override Task<AuthenticationScheme> GetDefaultSignOutSchemeAsync()
        {
            return GetSchemeAsync(IdentityConstants.ApplicationScheme);
        }
    }

    public static class ContextAwareAuthenticationSchemeExtensions
    {
        public static IServiceCollection AddContextAwareAuthenticationScheme(this IServiceCollection services, Action<ContextAwareAuthenticationSchemeOptions> configure)
        {
            services.Configure(configure);
            services.AddSingleton<IAuthenticationSchemeProvider, ContextAwareAuthenticationSchemeProvider>();
            services.AddSingleton<AuthenticationSchemeProvider, ContextAwareAuthenticationSchemeProvider>();
            return services;
        }
    }
}
