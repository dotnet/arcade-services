// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Xml;
using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ProductConstructionService.Common;

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

        List<string> missingDependencies = [];
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
                    missingDependencies.Add(dependency.Name);
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
        using var xmlWriter = XmlWriter.Create(stringWriter, xmlWriterSettings);
        output.Save(xmlWriter);

        if (missingDependencies.Count > 0)
        {
            StringBuilder sb = new();
            sb.AppendLine("The following dependencies were found in Version.Details.xml, but not in Version.props. Consider removing them");
            foreach (var dep in missingDependencies.Order())
            {
                sb.AppendLine($"  {dep}");
            }
            logger.LogWarning(sb.ToString());
        }

        logger.LogInformation(stringWriter.ToString());
    }

    public static IServiceCollection RegisterServices(IServiceCollection services)
    {
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(LogLevel.Information));
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
