using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.DotNet.Darc.Tests
{
    static internal class TestHelpers
    {
        static internal string NormalizeLineEndings(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }
    }
}
