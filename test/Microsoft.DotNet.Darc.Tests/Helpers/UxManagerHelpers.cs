// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Shouldly;
using Microsoft.DotNet.Darc.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Helpers;

[TestFixture]
public class UxManagerHelpers
{
    [Test]
    public void PathCodeLocation()
    {
        var codeCommand = UxManager.GetParsedCommand("code --wait");

        codeCommand.FileName.ShouldBe("code");
        codeCommand.Arguments.ShouldBe(" --wait");
    }

    [Test]
    public void EscapedCodeLocation()
    {
        var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe' --wait");

        codeCommand.FileName.ShouldBe(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        codeCommand.Arguments.ShouldBe(" --wait");
    }

    [Test]
    public void EscapedCodeLocationWithoutArg()
    {
        var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe'");

        codeCommand.FileName.ShouldBe(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        codeCommand.Arguments.ShouldBe("");
    }
}
