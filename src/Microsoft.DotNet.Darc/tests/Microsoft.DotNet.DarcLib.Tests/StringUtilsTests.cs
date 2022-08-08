// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests
{
    [TestFixture]
    public class StringUtilsTests
    {
        private static readonly char s_shellQuoteChar =
            (int) Environment.OSVersion.Platform != 128
                && Environment.OSVersion.Platform != PlatformID.Unix
                && Environment.OSVersion.Platform != PlatformID.MacOSX
            ? '"'   // Windows
            : '\''; // !Windows

        [Test]
        public void NoEscapingNeeded() => StringUtils.Quote("foo").Should().Be("foo");

        [TestCase("foo bar", "foo bar")]
        [TestCase("foo \"bar\"", "foo \\\"bar\\\"")]
        [TestCase("foo bar's", "foo bar\\\'s")]
        [TestCase("foo $bar's", "foo $bar\\\'s")]
        public void QuoteForProcessTest(string input, string expected)
            => StringUtils.Quote(input).Should().Be(s_shellQuoteChar + expected + s_shellQuoteChar);
    }
}
