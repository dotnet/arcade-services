// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VersionPropsFormatter;

public class VersionPropsFormatter(
    IVersionDetailsParser versionDetailsParser,
    IDependencyFileManager dependencyFileManager,
    IProcessManager processManager,
    ILogger<VersionPropsFormatter> logger)
{
    public async Task RunAsync(string path)
    {
        NativePath repoPath = new(processManager.FindGitRoot(path));
        VersionDetails versionDetails = versionDetailsParser.ParseVersionDetailsFile(repoPath / VersionFiles.VersionDetailsXml, includePinned: true);
        var versionDetailsLookup = versionDetails.Dependencies.ToLookup(dep => dep.RepoUri, dep => dep);

        XmlDocument versionProps = await dependencyFileManager.ReadVersionPropsAsync(repoPath, "HEAD");

        XmlDocument output = new();
        XmlElement propertyGroup = output.CreateElement("PropertyGroup");

        bool TryGetExistingDependencyNameInVersionProps(string dependencyName, out string nodeName)
        {
            nodeName = VersionFiles.GetVersionPropsPackageVersionElementName(dependencyName);
            if (DependencyFileManager.GetVersionPropsNode(versionProps, nodeName) != null)
            {
                return true;
            }
            nodeName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependencyName);
            return DependencyFileManager.GetVersionPropsNode(versionProps, nodeName) != null;
        }

        foreach (var repoDependencies in versionDetailsLookup)
        {
            var repoName = repoDependencies.Key.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            repoName = char.ToUpper(repoName[0]) + repoName.Substring(1);
            List<(string, string)> dependenciesToOutput = [];

            foreach (var dependency in repoDependencies.OrderBy(dep => dep.Name))
            {
                if (TryGetExistingDependencyNameInVersionProps(dependency.Name, out var nodeName))
                {
                    dependenciesToOutput.Add((nodeName, dependency.Version));
                }
                else
                {
                    logger.LogWarning("Dependency {name} found in Version.Details, but not in Version.Props, consider removing it", dependency.Name);
                }
            }

            if (dependenciesToOutput.Count > 0)
            {
                propertyGroup.AppendChild(output.CreateComment($" {repoName} dependencies "));
                foreach (var (name, version) in dependenciesToOutput)
                {
                    XmlElement element = output.CreateElement(name);
                    element.InnerText = version;
                    propertyGroup.AppendChild(element);
                }
            }
        }

        output.AppendChild(propertyGroup);

        XmlWriterSettings xmlWriterSettings = new()
        {
            Indent = true,
            NewLineOnAttributes = false,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true
        };

        using StringWriter stringWriter = new();
        using XmlWriter xmlWriter = XmlWriter.Create(stringWriter, xmlWriterSettings);
        output.Save(xmlWriter);

        logger.LogInformation(stringWriter.ToString());
    }

    public static IServiceCollection RegisterServices(IServiceCollection services)
    {
        services.AddLogging(options => options.AddConsole());
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<IProcessManager>>());
        services.AddSingleton<ITelemetryRecorder, NoTelemetryRecorder>();
        services.AddSingleton<IRemoteTokenProvider>(new RemoteTokenProvider());
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IGitRepo, LocalLibGit2Client>();
        services.AddSingleton<IDependencyFileManager, DependencyFileManager>();

        services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        services.AddSingleton<IVersionDetailsParser, VersionDetailsParser>();
        return services;
    }

}
