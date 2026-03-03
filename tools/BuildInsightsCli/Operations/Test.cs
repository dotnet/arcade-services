// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.Logging;
using Tools.Cli.Core;

namespace BuildInsightsCli.Operations;

[Verb("test", HelpText = "Test command for development purposes")]
[Operation<Test>]
public class TestOptions : Options
{
    [Option("name", Required = true, HelpText = "Name to be used in the test operation")]
    public string Name { get; set; } = string.Empty;
}

internal class Test(
    TestOptions options,
    ILogger<Test> logger
) : IOperation
{
    public Task<int> RunAsync()
    {
        logger.LogInformation("Test operation executed with name: {name}", options.Name);
        return Task.FromResult(0);
    }
}
