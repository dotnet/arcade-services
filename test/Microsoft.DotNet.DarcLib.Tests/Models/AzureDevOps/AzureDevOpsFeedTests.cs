// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

/// <summary>
/// Unit tests for AzureDevOpsFeed constructor covering property assignments, null/default handling,
/// and initialization behavior of the Packages collection.
/// </summary>
public class AzureDevOpsFeedTests
{
    /// <summary>
    /// Provides edge-case string inputs for account, id, and name parameters:
    /// - nulls
    /// - empty strings
    /// - whitespace-only strings
    /// - very long strings
    /// - strings with special/control/unicode characters
    /// </summary>
    public static IEnumerable ValidStringInputs
    {
        get
        {
            yield return new TestCaseData(null, null, null).SetName("NullValues");
            yield return new TestCaseData(string.Empty, string.Empty, string.Empty).SetName("EmptyStrings");
            yield return new TestCaseData(" ", "\t", " \r\n ").SetName("WhitespaceOnly");
            yield return new TestCaseData(new string('a', 5000), new string('b', 5000), new string('c', 5000)).SetName("VeryLongStrings");
            yield return new TestCaseData("acc😀\0\t\n", "id-汉字-ç-ø-ß", "pkg\nname\0end\t!@#$%^&*()").SetName("SpecialAndControlChars");
        }
    }

    /// <summary>
    /// Ensures the constructor assigns Account, Id, and Name exactly as provided (including null/edge strings),
    /// initializes Packages to an empty list, and sets Project to null when the optional parameter is omitted.
    /// Inputs:
    ///  - Various edge-case strings for account, id, and name.
    /// Expected:
    ///  - feed.Account == account
    ///  - feed.Id == id
    ///  - feed.Name == name
    ///  - feed.Project == null
    ///  - feed.Packages is non-null and empty
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidStringInputs))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_StringEdgeCases_PropertiesAssigned(string account, string id, string name)
    {
        // Arrange
        // (No additional arrangement needed beyond parameters)

        // Act
        var feed = new AzureDevOpsFeed(account, id, name);

        // Assert
        feed.Account.Should().Be(account);
        feed.Id.Should().Be(id);
        feed.Name.Should().Be(name);
        feed.Project.Should().BeNull();
        feed.Packages.Should().NotBeNull();
        feed.Packages.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that when a non-null project is supplied, the Project property references the same instance.
    /// Inputs:
    ///  - account/id/name with simple values
    ///  - a valid AzureDevOpsProject instance
    /// Expected:
    ///  - Project is the same instance as provided
    ///  - Other properties are assigned as provided
    ///  - Packages is initialized non-null and empty
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_ProjectProvided_ProjectPropertySet()
    {
        // Arrange
        var project = new AzureDevOpsProject("proj-name", "proj-id");
        var account = "account-x";
        var id = "feed-id";
        var name = "feed-name";

        // Act
        var feed = new AzureDevOpsFeed(account, id, name, project);

        // Assert
        feed.Project.Should().BeSameAs(project);
        feed.Account.Should().Be(account);
        feed.Id.Should().Be(id);
        feed.Name.Should().Be(name);
        feed.Packages.Should().NotBeNull();
        feed.Packages.Should().BeEmpty();
    }

    /// <summary>
    /// Ensures that each constructed instance gets its own Packages list (no shared static state).
    /// Inputs:
    ///  - Two different constructed feeds.
    ///  - One feed's Packages is modified by adding a null element.
    /// Expected:
    ///  - The other feed's Packages remains empty.
    ///  - The two Packages list instances are not the same reference.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_PackagesInitialized_IndependentPerInstance()
    {
        // Arrange
        var feed1 = new AzureDevOpsFeed("acc1", "id1", "name1");
        var feed2 = new AzureDevOpsFeed("acc2", "id2", "name2");

        // Act
        feed1.Packages.Add(null);

        // Assert
        feed1.Packages.Should().NotBeNull();
        feed2.Packages.Should().NotBeNull();

        feed1.Packages.Count.Should().Be(1);
        feed2.Packages.Should().BeEmpty();

        feed1.Packages.Should().NotBeSameAs(feed2.Packages);
    }
}
