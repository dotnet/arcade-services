// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;

namespace ProductConstructionService.ScenarioTests.Tests;

public class TestHelpersTests
{
    [Test]
    public void EmptyArguments()
    {
        var formatted = TestHelpers.FormatExecutableCall("darc.exe");

        formatted.Should().Be("darc.exe");
    }

    [Test]
    public void HarmlessArguments()
    {
        var formatted = TestHelpers.FormatExecutableCall("darc.exe", ["add-channel", "--name", "what-a-channel"]);

        formatted.Should().Be("darc.exe \"add-channel\" \"--name\" \"what-a-channel\"");
    }

    [Test]
    public void ArgumentsWithSecretTokensInside()
    {
        var formatted = TestHelpers.FormatExecutableCall("darc.exe", ["-p", "secret", "add-channel", "--github-pat", "another secret", "--name", "what-a-channel"]);

        formatted.Should().Be("darc.exe \"-p\" \"***\" \"add-channel\" \"--github-pat\" \"***\" \"--name\" \"what-a-channel\"");
    }
}
