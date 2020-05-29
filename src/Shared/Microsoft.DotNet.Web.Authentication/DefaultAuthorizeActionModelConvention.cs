// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Microsoft.DotNet.Web.Authentication
{
    public class DefaultAuthorizeActionModelConvention : IActionModelConvention
    {
        public DefaultAuthorizeActionModelConvention(string policyName)
        {
            Filter = new AuthorizeFilter(policyName);
        }

        public AuthorizeFilter Filter { get; }

        public void Apply(ActionModel action)
        {
            // ASP.NET 3.1 broke IAllowAnonymousFilter, just find the attribute ourselves
            // Otherwise it takes like 500 lines of code
            if (action.ActionMethod?.GetCustomAttributes(true).Any(a => a is IAllowAnonymous || a is IAuthorizeData) ?? false)
            {
                return;
            }

            IEnumerable<object> attributes = action.Controller.Attributes.Concat(action.Attributes);
            if (attributes.Any(a => a is IAllowAnonymous || a is IAuthorizeData))
            {
                return;
            }

            action.Filters.Add(Filter);
        }
    }
}
