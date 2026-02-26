// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.ServiceDefaults.Configuration.Models;

public class ServiceHookSettings
{
    public string SecretHttpHeaderName { get; set; }
    public string SecretHttpHeaderValue { get; set; }
}
