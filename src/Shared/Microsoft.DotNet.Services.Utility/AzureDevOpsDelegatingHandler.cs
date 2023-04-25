// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Services.Utility;

public abstract class AzureDevOpsDelegatingHandler : DelegatingHandler
{
    private readonly ILogger _logger;

    protected AzureDevOpsDelegatingHandler(ILogger logger)
    {
        _logger = logger;
    }

    protected string GetSingleHeader(HttpResponseMessage response, string header)
    {
        if (!response.Headers.TryGetValues(header, out IEnumerable<string> values))
        {
            return null;
        }

        using IEnumerator<string> e = values.GetEnumerator();
        if (!e.MoveNext())
        {
            _logger.LogError("Header {header} exists with a list of empty values", header);
            return null;
        }

        string returnValue = e.Current;

        if (!e.MoveNext())
        {
            return returnValue;
        }

        StringBuilder valueLog = new StringBuilder(returnValue);
        do
        {
            valueLog.Append(';').Append(e.Current);
        } 
        while (e.MoveNext());

        _logger.LogError("Header {header} exists with multiple values: '{values}'", header, valueLog);
        return null;
    }
}
