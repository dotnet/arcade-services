using Microsoft.DotNet.Darc.Helpers;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests.Helpers
{
    public class UxManagerHelpers
    {
        [Fact]
        public void PathCodeLocation()
        {
            var codeCommand = UxManager.GetParsedCommand("code --wait");

            Assert.Equal("code", codeCommand.FileName);
            Assert.Equal(" --wait", codeCommand.Arguments);
        }

        [Fact]
        public void EscapedCodeLocation()
        {
            var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe' --wait");

            Assert.Equal(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe", codeCommand.FileName);
            Assert.Equal(" --wait", codeCommand.Arguments);
        }

        [Fact]
        public void EscapedCodeLocationWithoutArg()
        {
            var codeCommand = UxManager.GetParsedCommand(@"'C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe'");

            Assert.Equal(@"C:\Users\lulansky\AppData\Local\Programs\Microsoft VS Code\Code.exe", codeCommand.FileName);
            Assert.Equal("", codeCommand.Arguments);
        }
    }
}
