// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public static class StringRegexAssertionExtensions{
        public static AndConstraint<StringAssertions> MatchRegex(
            this StringAssertions stringAssertion,
            Regex regularExpression,
            string because = "",
            params object[] becauseArgs
        )
        {
            Execute.Assertion.ForCondition(stringAssertion.Subject != null)
                .UsingLineBreaks.BecauseOf(because, becauseArgs)
                .FailWith("Expected {context:string} to match regex {0}{reason}, but it was <null>.",
                    (object) regularExpression);
            Execute.Assertion.ForCondition(regularExpression.IsMatch(stringAssertion.Subject))
                .BecauseOf(because, becauseArgs)
                .UsingLineBreaks
                .FailWith("Expected {context:string} to match regex {0}{reason}, but {1} does not match.",
                    regularExpression.ToString(),
                    stringAssertion.Subject);

            return new AndConstraint<StringAssertions>(stringAssertion);
        }
    }
}
