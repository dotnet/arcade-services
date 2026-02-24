// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Api;

public class HandleDuplicateKeyRowsAttribute : ActionFilterAttribute
{
    public HandleDuplicateKeyRowsAttribute(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }

    public string ErrorMessage { get; }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();
        if (executedContext.Exception is DbUpdateException dbEx &&
            // Note: From inspection this will throw Microsoft.Data.SqlClient.SqlException,
            //       not System.Data.SqlClient.SqlException; we will explicitly handle both.
            (dbEx.InnerException is Microsoft.Data.SqlClient.SqlException) &&
            dbEx.InnerException.Message.Contains("Cannot insert duplicate key"))
        {
            executedContext.Exception = null;

            var message = ErrorMessage;
            foreach (var argument in context.ActionArguments)
            {
                message = message.Replace("{" + argument.Key + "}", argument.Value?.ToString());
            }

            executedContext.Result =
                new ObjectResult(new ApiError(message)) { StatusCode = (int)HttpStatusCode.Conflict };
        }
    }
}
