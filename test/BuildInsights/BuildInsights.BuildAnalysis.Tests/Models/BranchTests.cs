// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Models;

[TestFixture]
public class BranchTests
{
    [TestCase("main")]
    [TestCase("pr/1234")]
    public void BranchParseTest(string name)
    {
        Branch branch = Branch.Parse(name);

        branch.Should().NotBeNull();
        branch.BranchName.Should().Be(name);
    }

    [TestCase(null)]
    [TestCase("")]
    public void BranchParseNullTest(string branchName)
    {
        Action a = () => Branch.Parse(branchName);

        a.Should().Throw<ArgumentNullException>();
    }

    [TestCase("main", "main", true)]
    [TestCase("main", "production", false)]
    public void BranchnameEqualityTest(string leftBranchName, string rightBranchName, bool expectedResult)
    {
        Branch leftBranch = Branch.Parse(leftBranchName);
        Branch rightBranch = Branch.Parse(rightBranchName);

        bool result = leftBranch.Equals(rightBranch);

        result.Should().Be(expectedResult);
    }

    [Test]
    public void BranchnameEqualityNullTest()
    {
        Branch leftBranch = Branch.Parse("main");
        Branch rightBranch = null;

        bool result = leftBranch.Equals(rightBranch);

        result.Should().Be(false);
    }

    [Test]
    public void BranchIsSerializableTest()
    {
        Branch branch = Branch.Parse("main");

        string serializedBranch = JsonSerializer.Serialize(branch);

        serializedBranch.Should().Be("\"main\"");
    }

    [Test]
    public void BranchIsDeserializableTest()
    {
        string branchName = "\"main\"";

        Branch branch = JsonSerializer.Deserialize<Branch>(branchName);

        branch.BranchName.Should().Be("main");
    }

    [TestCase("refs/heads/main")]
    [TestCase("refs/pulls/*")]
    public void GitRefParseTest(string name)
    {
        GitRef branch = GitRef.Parse(name);

        branch.Path.Should().Be(name);
    }

    [Test]
    public void GitRefParseNullErrorTest()
    {
        Action a = () => GitRef.Parse(null);

        a.Should().Throw<ArgumentNullException>();
    }

    [TestCase("")]
    [TestCase("abcd")]
    public void GitRefParseErrorTest(string badRefName)
    {
        Action a = () => GitRef.Parse(badRefName);

        a.Should().Throw<FormatException>();
    }

    [TestCase("main", "refs/heads/main")]
    [TestCase("prs/1234", "refs/heads/prs/1234")]
    public void BranchAsRefTest(string branchName, string expectedRef)
    {
        Branch branch = Branch.Parse(branchName);

        branch.Path.Should().Be(expectedRef);
    }

    [TestCase("refs/heads/main", "refs/heads/main", true)]
    [TestCase("refs/other/namespace", "refs/other/namespace", true)]
    [TestCase("refs/heads/main", "refs/heads/production", false)]
    public void GitrefEqualityTest(string leftName, string rightName, bool expectedResult)
    {
        Branch left = Branch.Parse(leftName);
        Branch right = Branch.Parse(rightName);

        bool result = left.Equals(right);

        result.Should().Be(expectedResult);
    }

    [Test]
    public void GitrefEqualityNullTest()
    {
        Branch left = Branch.Parse("refs/heads/main");
        Branch right = null;

        bool result = left.Equals(right);

        result.Should().Be(false);
    }

    [Test]
    public void GitrefBranchObjectEqualityTest()
    {
        object branch = Branch.Parse("main");
        GitRef gitRef = GitRef.Parse("refs/heads/main");

        gitRef.Equals(branch).Should().BeTrue();
    }

    [Test]
    public void GitrefAsBranchTest()
    {
        string refname = "refs/heads/main";
        GitRef gitRef = GitRef.Parse(refname);

        gitRef.Should().BeOfType<Branch>();
    }
}
