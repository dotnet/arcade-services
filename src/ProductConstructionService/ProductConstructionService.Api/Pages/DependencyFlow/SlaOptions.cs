// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Pages.DependencyFlow;

public class SlaOptions
{
    public IDictionary<string, Sla> Repositories { get; } =
        new Dictionary<string, Sla>
        {
            ["[Default]"] = new() { FailUnconsumedBuildAge = 7, WarningUnconsumedBuildAge = 5 },
        };

    public Sla GetForRepo(string repoShortName)
    {
        if (!Repositories.TryGetValue("dotnet/" + repoShortName, out var value))
        {
            value = Repositories["[Default]"];
        }

        return value;
    }
}
