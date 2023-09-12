// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Maestro.ContainerApp.Utils;

public class ValidateModelStateAttribute : ActionFilterAttribute
{
    public ValidateModelStateAttribute()
    {
        Order = int.MaxValue;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            IEnumerable<string> errors = context.ModelState.Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage);
            context.Result = new BadRequestObjectResult(new ApiError("The request is invalid", errors));
        }
    }
}
