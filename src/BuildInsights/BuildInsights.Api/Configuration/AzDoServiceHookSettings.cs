// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
using BuildInsights;

namespace BuildInsights.Api.Configuration;

public class AzDoServiceHookSettings
{
    public string SecretHttpHeaderName { get; set; }
    public string SecretHttpHeaderValue { get; set; }
}
