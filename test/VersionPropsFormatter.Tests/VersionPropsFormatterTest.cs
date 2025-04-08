// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace VersionPropsFormatter.Tests;

[TestFixture]
public class VersionPropsFormatterTest
{
    private IServiceProvider _serviceProvider = null!;
    private Mock<ILogger<VersionPropsFormatter>> _loggerMock = null!;

    private const string VersionDetails = """
        <Dependencies>
          <ProductDependencies>
            <Dependency Name="Foo" Version="1.0.0">
              <Uri>https://github.com/dotnet/foo</Uri>
              <Sha>sha1</Sha>
            </Dependency>
            <Dependency Name="Foo2" Version="2.0.0">
              <Uri>https://github.com/dotnet/foo</Uri>
              <Sha>sha1</Sha>
            </Dependency>
            <Dependency Name="Bar" Version="1.0.0">
              <Uri>https://github.com/dotnet/bar</Uri>
              <Sha>sha1</Sha>
            </Dependency>
          </ProductDependencies>
          <ToolsetDependencies>
          </ToolsetDependencies>
        </Dependencies>
        """;

    private const string VersionProps = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project>
          <PropertyGroup>
          </PropertyGroup>
          <!--Package versions-->
          <PropertyGroup>
            <FooPackageVersion>1.0.0</FooPackageVersion>
            <BarPackageVersion>1.0.0</BarPackageVersion>
          </PropertyGroup>
        </Project>
        """;
    private const string ExpectedWarning = """
    The following dependencies were found in Version.Details.xml, but not in Version.props. Consider removing them
      Foo2
    """;
    private const string ExpectedOutput = """
        <PropertyGroup>
          <!-- Foo dependencies -->
          <FooPackageVersion>1.0.0</FooPackageVersion>
          <!-- Bar dependencies -->
          <BarPackageVersion>1.0.0</BarPackageVersion>
        </PropertyGroup>
        """;

    [SetUp]
    public void Setup()
    {
        IServiceCollection serviceCollection = new ServiceCollection();
        VersionPropsFormatter.RegisterServices(serviceCollection);
        _loggerMock = new Mock<ILogger<VersionPropsFormatter>>();
        serviceCollection.AddSingleton(_loggerMock.Object);

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Test]
    public async Task TestVersionPropsFormatter()
    {
        NativePath tmpFolder = new(Path.Combine(Path.GetTempPath(), Path.GetTempFileName()));
        IProcessManager processManager = _serviceProvider.GetRequiredService<IProcessManager>();

        try
        {
            if (Directory.Exists(tmpFolder))
            {
                Directory.Delete(tmpFolder, true);
            }
            if (File.Exists(tmpFolder))
            {
                File.Delete(tmpFolder);
            }
            Directory.CreateDirectory(tmpFolder);
            await processManager.ExecuteGit(tmpFolder, "init");
            Directory.CreateDirectory(tmpFolder / Constants.EngFolderName);

            File.WriteAllText(tmpFolder / VersionFiles.VersionDetailsXml, VersionDetails);
            File.WriteAllText(tmpFolder / VersionFiles.VersionProps, VersionProps);

            await processManager.ExecuteGit(tmpFolder, "add", "--all");
            await processManager.ExecuteGit(tmpFolder, "commit", "-m", "Initial commit");

            await ActivatorUtilities.CreateInstance<VersionPropsFormatter>(_serviceProvider).RunAsync(tmpFolder);

            // Verify logger calls
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(ExpectedWarning)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(ExpectedOutput)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tmpFolder))
            {
                GitFile.MakeGitFilesDeletable(tmpFolder);
                Directory.Delete(tmpFolder, true);
            }
        }
    }
}
