// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Collections;
using FluentAssertions.Execution;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public static class CollectionAssertionExtension
    {
        // Temporary placeholder for https://github.com/fluentassertions/fluentassertions/issues/1179
        public static AndConstraint<TAssertions> AllSatisfy<T, TAssertions>(
            this CollectionAssertions<IEnumerable<T>, TAssertions> assertions,
            Action<T> inspector,
            string because = "",
            params object[] becauseArgs) where TAssertions : CollectionAssertions<IEnumerable<T>, TAssertions>
        {
            if (inspector == null) throw new ArgumentNullException(nameof(inspector));

            Execute.Assertion.BecauseOf(because, becauseArgs)
                .WithExpectation("Expected {context:collection} to satisfy inspector{reason}, ")
                .ForCondition(assertions.Subject != null)
                .FailWith("but collection is <null>.")
                .Then.ForCondition(assertions.Subject.Any())
                .FailWith("but collection is empty.")
                .Then.ClearExpectation();

            string[] strArray = assertions.CollectFailuresFromInspectors(inspector);
            if (strArray.Any())
            {
                string formattedFailReason = Environment.NewLine +
                    string.Join(Environment.NewLine, strArray.Select(x => x.IndentLines()));
                Execute.Assertion.BecauseOf(because, becauseArgs)
                    .AddPreFormattedFailure(
                        "Expected {context:collection} to satisfy all inspectors{reason}, but inspector is not satisfied:" +
                        formattedFailReason);
            }

            return new AndConstraint<TAssertions>((TAssertions) assertions);
        }


        private static string[] CollectFailuresFromInspectors<T, TAssertions>(
            this CollectionAssertions<IEnumerable<T>, TAssertions> assertions,
            Action<T> inspector) where TAssertions : CollectionAssertions<IEnumerable<T>, TAssertions>
        {
            using (AssertionScope assertionScope1 = new AssertionScope())
            {
                int num = 0;
                foreach (var obj in assertions.Subject)
                {
                    string[] strArray;
                    using (AssertionScope assertionScope2 = new AssertionScope())
                    {
                        inspector(obj);
                        strArray = assertionScope2.Discard();
                    }

                    if (strArray.Length != 0)
                    {
                        string str = string.Join(Environment.NewLine,
                            strArray.Select(x => x.IndentLines().TrimEnd('.')));
                        assertionScope1.AddPreFormattedFailure($"At index {num}:{Environment.NewLine}{str}");
                    }

                    ++num;
                }

                return assertionScope1.Discard();
            }
        }

        public static string IndentLines(this string value)
        {
            return string.Join(Environment.NewLine,
                value.Split(new[]
                        {
                            '\r',
                            '\n'
                        },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => "\t" + x));
        }
    }
}
