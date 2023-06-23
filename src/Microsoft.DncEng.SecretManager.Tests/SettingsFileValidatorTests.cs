using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DncEng.SecretManager.Tests;

public class SettingsFileValidatorTests
{
    private class Data : IDisposable
    {
        public TestConsole Console { get; set; }
        public Mock<SecretTypeRegistry> SecretRegistry { get; set; }
        public SettingsFileValidator Validator { get; set; }
        public ServiceProvider Provider { get; set; }

        public void Dispose()
        {
            Provider?.Dispose();
        }
    }

    private Data CreateTestData()
    {
        var d = new Data
        {
            Console = new TestConsole(),
            SecretRegistry = new Mock<SecretTypeRegistry>(),
        };
        var type = new Mock<SecretType>();
        type.Setup(t => t.GetCompositeSecretSuffixes())
            .Returns(new List<string> {""});
        d.SecretRegistry.Setup(r => r.Get("type"))
            .Returns(type.Object);

        var services = new ServiceCollection();
        services.AddSingleton<IConsole>(d.Console);
        services.AddSingleton(d.SecretRegistry.Object);
        services.AddSingleton<SettingsFileValidator>();
        d.Provider = services.BuildServiceProvider();
        d.Validator = d.Provider.GetRequiredService<SettingsFileValidator>();
        return d;
    }

    [Test]
    public async Task MissingSettingFails()
    {
        using var data = CreateTestData();

        using var manifest = new TemporaryFile();
        using var baseSettings = new TemporaryFile();
        using var envSettings = new TemporaryFile();

        var vaultName = "somevault";
        var sub = Guid.Empty.ToString("D");

        await manifest.WriteAllTextAsync($@"
storageLocation:
  type: azure-key-vault
  parameters:
    name: {vaultName}
    subscription: {sub}
secrets:
  one:
    type: type
    parameters:
      p: 1
");

        await envSettings.WriteAllTextAsync($@"
{{
  ""KeyVaultUri"": ""https://{vaultName}.vault.azure.net""
}}
");

        await baseSettings.WriteAllTextAsync($@"
{{
  ""first"": ""[vault(one)]"",
  ""second"": ""[vault(two)]""
}}
");

        (await data.Validator.Invoking(v => v.ValidateFileAsync(envSettings.FilePath, baseSettings.FilePath, manifest.FilePath, CancellationToken.None))
                .Should()
                .CompleteWithinAsync(new TimeSpan(0, 0, 5)))
            .Subject.Should().BeFalse();
        data.Console.Errors.Should().HaveCountGreaterOrEqualTo(1).And.Subject.First().message.Should().Contain("Secret 'two' does not exist in manifest file.");
    }

    [Test]
    public async Task SettingOverriddenByEnvironmentIsntRequired()
    {
        using var data = CreateTestData();

        using var manifest = new TemporaryFile();
        using var baseSettings = new TemporaryFile();
        using var envSettings = new TemporaryFile();

        var vaultName = "somevault";
        var sub = Guid.Empty.ToString("D");

        await manifest.WriteAllTextAsync($@"
storageLocation:
  type: azure-key-vault
  parameters:
    name: {vaultName}
    subscription: {sub}
secrets:
  one:
    type: type
    parameters:
      p: 1
");

        await envSettings.WriteAllTextAsync($@"
{{
  ""KeyVaultUri"": ""https://{vaultName}.vault.azure.net"",
  ""second"": ""Specific value""
}}
");

        await baseSettings.WriteAllTextAsync($@"
{{
  ""first"": ""[vault(one)]"",
  ""second"": ""[vault(two)]""
}}
");

        (await data.Validator.Invoking(v => v.ValidateFileAsync(envSettings.FilePath, baseSettings.FilePath, manifest.FilePath, CancellationToken.None))
                .Should()
                .CompleteWithinAsync(new TimeSpan(0, 0, 5)))
            .Subject.Should().BeTrue();
        data.Console.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task GoodSettingsSucceed()
    {
        using var data = CreateTestData();

        using var manifest = new TemporaryFile();
        using var baseSettings = new TemporaryFile();
        using var envSettings = new TemporaryFile();

        var vaultName = "somevault";
        var sub = Guid.Empty.ToString("D");

        await manifest.WriteAllTextAsync($@"
storageLocation:
  type: azure-key-vault
  parameters:
    name: {vaultName}
    subscription: {sub}
secrets:
  one:
    type: type
    parameters:
      p: 1
  two:
    type: type
    parameters:
      p: 2
");

        await envSettings.WriteAllTextAsync($@"
{{
  ""KeyVaultUri"": ""https://{vaultName}.vault.azure.net""
}}
");

        await baseSettings.WriteAllTextAsync($@"
{{
  ""first"": ""[vault(one)]"",
  ""second"": ""[vault(two)]""
}}
");

        (await data.Validator.Invoking(v => v.ValidateFileAsync(envSettings.FilePath, baseSettings.FilePath, manifest.FilePath, CancellationToken.None))
                .Should()
                .CompleteWithinAsync(new TimeSpan(0, 0, 5)))
            .Subject.Should().BeTrue();
        data.Console.Errors.Should().BeEmpty();
    }
}
