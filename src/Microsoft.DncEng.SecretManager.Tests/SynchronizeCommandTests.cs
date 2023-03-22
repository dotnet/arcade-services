using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.Commands;
using Microsoft.DncEng.SecretManager.StorageTypes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class SynchronizeCommandTests
{
    private async Task TestCommand(DateTimeOffset now, string manifestText, string locationTypeName, List<SecretProperties> existingSecrets, string secretTypeName, List<string> suffixes,
        Func<IDictionary<string, object>, List<string>> referencesGenerator, List<List<SecretData>> rotationResults, List<(string name, SecretValue value)> expectedSets)
    {
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IConsole>());

        var storageLocationTypeRegistry = new Mock<StorageLocationTypeRegistry>(MockBehavior.Strict);
        services.AddSingleton(p => storageLocationTypeRegistry.Object);
        var secretTypeRegistry = new Mock<SecretTypeRegistry>(MockBehavior.Strict);
        services.AddSingleton(p => secretTypeRegistry.Object);
        var clock = new Mock<ISystemClock>(MockBehavior.Strict);
        services.AddSingleton(p => clock.Object);
        services.AddSingleton<SynchronizeCommand>();

        var provider = services.BuildServiceProvider();

        var manifestFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(manifestFile, manifestText, cancellationToken);
            clock.Setup(c => c.UtcNow).Returns(now);

            var storageLocationType = new Mock<StorageLocationType>(MockBehavior.Strict);
            storageLocationType.Protected().Setup("Dispose", true);
            storageLocationType
                .Setup(storage => storage.ListSecretsAsync(It.IsAny<IDictionary<string, object>>()))
                .ReturnsAsync(existingSecrets);
            var actualSetNames = new List<string>();
            var actualSetValues = new List<SecretValue>();
            storageLocationType
                .Setup(storage => storage.SetSecretValueAsync(It.IsAny<IDictionary<string, object>>(), Capture.In(actualSetNames), Capture.In(actualSetValues)))
                .Returns(Task.CompletedTask);
            storageLocationTypeRegistry
                .Setup(slt => slt.Get(locationTypeName))
                .Returns(storageLocationType.Object);

            var secretType = new Mock<SecretType>(MockBehavior.Strict);
            secretType.Protected().Setup("Dispose", true);
            secretType
                .Setup(t => t.GetCompositeSecretSuffixes())
                .Returns(suffixes);
            var currentIndex = 0;
            secretType
                .Setup(t => t.RotateValues(It.IsAny<IDictionary<string, object>>(), It.IsAny<RotationContext>(), cancellationToken))
                .ReturnsAsync(() => rotationResults[currentIndex++]);
            secretType.Setup(t => t.GetSecretReferences(It.IsAny<IDictionary<string, object>>()))
                .Returns(referencesGenerator);

            secretTypeRegistry
                .Setup(registry => registry.Get(secretTypeName))
                .Returns(secretType.Object);

            var command = provider.GetRequiredService<SynchronizeCommand>();
            command.HandlePositionalArguments(new List<string> {manifestFile});

            await command.RunAsync(cancellationToken);

            var index = 0;
            actualSetNames.Count.Should().Be(expectedSets.Count);
            actualSetValues.Count.Should().Be(expectedSets.Count);
            foreach (var (expectedName, expectedValue) in expectedSets)
            {
                actualSetNames[index].Should().BeEquivalentTo(expectedName);
                actualSetValues[index].Should().BeEquivalentTo(expectedValue);
                index++;
            }

        }
        finally
        {
            File.Delete(manifestFile);
        }
    }

    [Test]
    public async Task ExpiredSecretsGetRotated()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);
        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  expired:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("expired", now.AddDays(-5),
                    ImmutableDictionary.Create<string, string>()),
            },
            "test-secret",
            new List<string>{""},
            parameters => new List<string>(),
            new List<List<SecretData>>
            {
                new List<SecretData>
                {
                    new SecretData("test-value", now.AddDays(5), now.AddDays(2))
                }
            }, new List<(string name, SecretValue value)>
            {
                ("expired", new SecretValue("test-value", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5)))
            });
    }

    [Test]
    public async Task ToBeRotatedSecretGetsRotated()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);

        var dic = new Dictionary<string, string>
        {
            { AzureKeyVault.NextRotationOnTag, now.AddDays(-1).ToString() }
        };

        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  normal:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("normal", now.AddDays(10), dic.ToImmutableDictionary()),
            },
            "test-secret",
            new List<string>{""},
            parameters => new List<string>(),
            new List<List<SecretData>>
            {
                new List<SecretData>
                {
                    new SecretData("test-value", now.AddDays(20), now.AddDays(5))
                }
            }, new List<(string name, SecretValue value)>
            {
                ("normal", new SecretValue("test-value", dic.ToImmutableDictionary(), now.AddDays(5), now.AddDays(20)))
            });
    }

    [Test]
    public async Task ValidSecretIsntRotated()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);
        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  normal:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("normal", now.AddDays(10),
                    ImmutableDictionary.Create<string, string>()),
            },
            "test-secret",
            new List<string>{""},
            parameters => new List<string>(),
            new List<List<SecretData>>
            {
            }, new List<(string name, SecretValue value)>
            {
            });
    }

    [Test]
    public async Task CompositeSecretRotatesAll()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);

        var dic = new Dictionary<string, string>
        {
            { AzureKeyVault.NextRotationOnTag, now.AddDays(-2).ToString() }
        };

        var dic2 = new Dictionary<string, string>
        {
            { AzureKeyVault.NextRotationOnTag, now.AddDays(-2).ToString() }
        };

        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  normal:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("normal", now.AddDays(10), dic.ToImmutableDictionary()),
                new SecretProperties("normal-2", now.AddDays(10), dic2.ToImmutableDictionary()),
            },
            "test-secret",
            new List<string>{"", "-2"},
            parameters => new List<string>(),
            new List<List<SecretData>>
            {
                new List<SecretData>
                {
                    new SecretData("test-value-1", now.AddDays(20), now.AddDays(5)),
                    new SecretData("test-value-2", now.AddDays(20), now.AddDays(5)),
                }
            }, new List<(string name, SecretValue value)>
            {
                ("normal", new SecretValue("test-value-1", dic.ToImmutableDictionary(), now.AddDays(5), now.AddDays(20))),
                ("normal-2", new SecretValue("test-value-2", dic2.ToImmutableDictionary(), now.AddDays(5), now.AddDays(20))),
            });
    }

    [Test]
    public async Task SecretsAreRotatedInCorrectOrderAndAllTriggeredByExpiredMainSecret()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);
        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  grand-child:
    type: test-secret
    parameters:
        dependsOnSecret: child
  child:
    type: test-secret
    parameters:
        dependsOnSecret: expired-main
  expired-main:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("expired-main", now.AddDays(-5),
                    ImmutableDictionary.Create<string, string>()),
                new SecretProperties("child", now.AddDays(5),
                    ImmutableDictionary.Create<string, string>()),
                new SecretProperties("grand-child", now.AddDays(5),
                    ImmutableDictionary.Create<string, string>()),
            },
            "test-secret",
            new List<string> { "" },
            parameters => parameters == null ? new List<string>() : new List<string> { parameters["dependsOnSecret"].ToString() },
            new List<List<SecretData>>
            {
                new List<SecretData>
                {
                    new SecretData("test-value-1", now.AddDays(5), now.AddDays(2)),
                },
                new List<SecretData>
                {
                    new SecretData("test-value-2", now.AddDays(5), now.AddDays(2)),
                },
                new List<SecretData>
                {
                    new SecretData("test-value-3", now.AddDays(5), now.AddDays(2)),
                }
            }, new List<(string name, SecretValue value)>
            {
                ("expired-main", new SecretValue("test-value-1", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5))),
                ("child", new SecretValue("test-value-2", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5))),
                ("grand-child", new SecretValue("test-value-3", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5)))
            });
    }

    [Test]
    public async Task SecretsAreRotatedInCorrectOrderAndGrandChildTriggeredByExpiredChildSecret()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);
        await TestCommand(now, @"
storageLocation:
  type: test
secrets:
  grand-child:
    type: test-secret
    parameters:
        dependsOnSecret: expired-child
  expired-child:
    type: test-secret
    parameters:
        dependsOnSecret: main
  main:
    type: test-secret
", "test",
            new List<SecretProperties>
            {
                new SecretProperties("main", now.AddDays(5),
                    ImmutableDictionary.Create<string, string>()),
                new SecretProperties("expired-child", now.AddDays(-5),
                    ImmutableDictionary.Create<string, string>()),
                new SecretProperties("grand-child", now.AddDays(5),
                    ImmutableDictionary.Create<string, string>()),
            },
            "test-secret",
            new List<string> { "" },
            parameters => parameters == null ? new List<string>() : new List<string> { parameters["dependsOnSecret"].ToString() },
            new List<List<SecretData>>
            {
                new List<SecretData>
                {
                    new SecretData("test-value-1", now.AddDays(5), now.AddDays(2)),
                },
                new List<SecretData>
                {
                    new SecretData("test-value-2", now.AddDays(5), now.AddDays(2)),
                }

            }, new List<(string name, SecretValue value)>
            {
                ("expired-child", new SecretValue("test-value-1", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5))),
                ("grand-child", new SecretValue("test-value-2", ImmutableDictionary.Create<string, string>(), now.AddDays(2), now.AddDays(5)))
            });
    }

    [Test]
    public void InvalidSecretReferenceThrowsException()
    {
        var now = DateTimeOffset.ParseExact("03/25/2021 1:30", "MM/dd/yyyy m:ss", null);
        Assert.ThrowsAsync<FailWithExitCodeException>(() =>
            TestCommand(now, @"
    storageLocation:
      type: test
    secrets:
      main:
        type: test-secret
      expired-child:
        type: test-secret
        parameters:
            dependsOnSecret: X
    ", "test",
                new List<SecretProperties>
                {
                    new SecretProperties("main", now.AddDays(5),
                        ImmutableDictionary.Create<string, string>()),
                    new SecretProperties("expired-child", now.AddDays(-5),
                        ImmutableDictionary.Create<string, string>()),
                },
                "test-secret",
                new List<string> { "" },
                parameters => parameters == null ? new List<string>() : new List<string> { parameters["dependsOnSecret"].ToString() },
                new List<List<SecretData>>
                {
                }, new List<(string name, SecretValue value)>
                {
                }));
    }
}
