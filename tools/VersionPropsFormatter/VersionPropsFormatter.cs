// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Xml;
using Maestro.Common;
using Maestro.Common.Telemetry;
using Microsoft.Build.Construction;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace VersionDetailsPropsFormatter;

public class VersionDetailsPropsFormatter(
    IVersionDetailsParser versionDetailsParser,
    IProcessManager processManager,
    ILogger<VersionDetailsPropsFormatter> logger)
{
    public void Run(string path)
    {
        NativePath repoPath = new(processManager.FindGitRoot(path));
        VersionDetails versionDetails = versionDetailsParser.ParseVersionDetailsFile(repoPath / VersionFiles.VersionDetailsXml, includePinned: true);

        var versionDetailsPropsContent = DependencyFileManager.GenerateVersionDetailsProps(versionDetails);
        WriteXml(repoPath / VersionFiles.VersionDetailsProps, versionDetailsPropsContent);

        var versionDetailsProps = ProjectRootElement.Open(repoPath / VersionFiles.VersionDetailsProps);
        var versionProps = ProjectRootElement.Open(repoPath / VersionFiles.VersionsProps);

        var conflictingProps = ExtractNonConditionalNonEmptyProperties(versionProps)
            .Intersect(ExtractNonConditionalNonEmptyProperties(versionDetailsProps))
            .ToList();

        if (conflictingProps.Count > 0)
        {
            StringBuilder sb = new();
            sb.AppendLine("Conflicting properties found in Versions.props, please delete them");
            foreach (var conflictingProp in conflictingProps)
            {
                sb.AppendLine($"- {conflictingProp}");
            }
            logger.LogWarning(sb.ToString());
        }

        if (!CheckForVersionDetailsPropsImport(versionProps))
        {
            logger.LogWarning("Please import `Version.Details.props` in the beginning of Versions.props");
        }
    }

    private static HashSet<string> ExtractNonConditionalNonEmptyProperties(ProjectRootElement msbuildFile)
        => msbuildFile.PropertyGroups
            .Where(group => string.IsNullOrEmpty(group.Condition))
            .SelectMany(group => group.Properties)
            .Where(prop => string.IsNullOrEmpty(prop.Condition))
            .Select(prop => prop.Name)
            .ToHashSet();

    private static bool CheckForVersionDetailsPropsImport(ProjectRootElement versionsProps) =>
        versionsProps.Imports.Any(import =>
            import.Project.Equals(Constants.VersionDetailsProps));

    private static void WriteXml(string path, XmlDocument document)
    {
        XmlWriterSettings xmlWriterSettings = new()
        {
            Indent = true,
            NewLineOnAttributes = false,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true
        };

        using var xmlWriter = XmlWriter.Create(path, xmlWriterSettings);
        document.Save(xmlWriter);
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
