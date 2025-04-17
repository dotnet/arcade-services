// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using System.Collections.Generic;

namespace Microsoft.Extensions.Options.Tests
{
    public class RequirementsTests
    {
        [Fact]
        public void ShouldValidateRequirements()
        {
            var requirements = new List<string> { "Requirement1", "Requirement2" };
            requirements.ShouldNotBeEmpty();
            requirements.ShouldContain("Requirement1");
            requirements.ShouldContain("Requirement2");
        }

        [Fact]
        public void ShouldFailValidationForEmptyRequirements()
        {
            var requirements = new List<string>();
            requirements.ShouldBeEmpty();
        }
    }
}