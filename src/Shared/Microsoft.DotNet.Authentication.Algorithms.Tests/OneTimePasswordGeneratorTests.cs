using System;
using Microsoft.DotNet.Authentication.Algorithms;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class OneTimePasswordGeneratorTests
{
    [Test]
    public void Constructor_InvalidSecret_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new OneTimePasswordGenerator("MFRGGZDFMZTWQ2L0"));
    }

    [Test]
    public void Generate_With29sDelay_PasswordsAreTheSame()
    {
        var passwordGenerator = new OneTimePasswordGenerator("MFRGGZDFMZTWQ2LK");

        var initialTimestamp = new DateTime(2021, 3, 1, 13, 15, 0, DateTimeKind.Utc);
        var intialPassword = passwordGenerator.Generate(initialTimestamp);
        var passwordAfter29s = passwordGenerator.Generate(initialTimestamp.AddSeconds(29));

        Assert.AreEqual("650100", intialPassword);
        Assert.AreEqual("650100", passwordAfter29s);
    }

    [Test]
    public void Generate_With30sDelay_PasswordsAreDifferent()
    {
        var passwordGenerator = new OneTimePasswordGenerator("MFRGGZDFMZTWQ2LK");

        var initialTimestamp = new DateTime(2021, 3, 1, 13, 15, 0, DateTimeKind.Utc);
        var intialPassword = passwordGenerator.Generate(initialTimestamp);
        var passwordAfter30s = passwordGenerator.Generate(initialTimestamp.AddSeconds(30));

        Assert.AreEqual("650100", intialPassword);
        Assert.AreEqual("019584", passwordAfter30s);
    }
}
