using FluentAssertions;
using Microsoft.DotNet.Darc.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Helpers
{
    [TestFixture]
    public class UxManagerHelpers
    {
        [Test]
        public void PathCodeLocation()
        {
            var codeCommand = UxManager.GetParsedCommand("code --wait");

            codeCommand.FileName.Should().Be("code");
            codeCommand.Arguments.Should().Be(" --wait");
        }

        [Test]
        public void EscapedCodeLocation()
        {
            var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe' --wait");

            codeCommand.FileName.Should().Be(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe");
            codeCommand.Arguments.Should().Be(" --wait");
        }

        [Test]
        public void EscapedCodeLocationWithoutArg()
        {
            var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe'");

            codeCommand.FileName.Should().Be(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe");
            codeCommand.Arguments.Should().Be("");
        }
    }
}
