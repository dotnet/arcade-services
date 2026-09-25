// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.PopUps;

/// <summary>
/// Common class for subscription management popups.
/// </summary>
internal abstract class SubscriptionPopUp<TData> : EditorPopUp where TData : SubscriptionPopUpData
{
    protected readonly TData _data;
    private readonly bool _forceCreation;
    private readonly IEnumerable<string> _suggestedChannels;
    private readonly IEnumerable<string> _suggestedRepositories;
    private readonly IEnumerable<string> _availableUpdateFrequencies;
    private readonly ILogger _logger;
    private readonly IGitRepoFactory _gitRepoFactory;

    public string Channel => _data.Channel;
    public string SourceRepository => _data.SourceRepository;
    public string TargetRepository => _data.TargetRepository;
    public string TargetBranch => _data.TargetBranch;
    public string UpdateFrequency => _data.UpdateFrequency;
    public bool MergePrs => bool.Parse(_data.MergePrs);
    public IReadOnlyCollection<string> IgnoredChecks => _data.IgnoredChecks;
    public bool Batchable => bool.Parse(_data.Batchable);
    public string? FailureNotificationTags => _data.FailureNotificationTags;
    public bool SourceEnabled => bool.Parse(_data.SourceEnabled);
    public bool AutoApprove => bool.Parse(_data.AutoApprove);
    public IReadOnlyCollection<string> ExcludedAssets => _data.ExcludedAssets;
    public string? SourceDirectory => _data.SourceDirectory;
    public string? TargetDirectory => _data.TargetDirectory;

    protected SubscriptionPopUp(
        string path,
        bool forceCreation,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        ILogger logger,
        IGitRepoFactory gitRepoFactory,
        TData data,
        IEnumerable<Line> header)
        : base(path)
    {
        _data = data;
        _forceCreation = forceCreation;
        _suggestedChannels = suggestedChannels;
        _suggestedRepositories = suggestedRepositories;
        _availableUpdateFrequencies = availableUpdateFrequencies;
        _logger = logger;
        _gitRepoFactory = gitRepoFactory;
        GeneratePopUpContent(header);
    }

    private void GeneratePopUpContent(IEnumerable<Line> header)
    {
        Contents.AddRange(header);

        ISerializer serializer = new SerializerBuilder().Build();
        string yaml = serializer.Serialize(_data);
        string[] lines = yaml.Split(Environment.NewLine);

        foreach (string line in lines)
        {
            if (line.StartsWith(SubscriptionPopUpData.SourceEnabledElement))
            {
                Contents.AddRange(
                [
                    new(),
                    new("Properties for code-enabled subscriptions (VMR code flow related):", true),
                ]);
            }

            Contents.Add(new Line(line));
        }

        Contents.Add(new($"Suggested repository URLs for '{SubscriptionPopUpData.SourceRepoElement}' or '{SubscriptionPopUpData.TargetRepoElement}':", true));

        foreach (string suggestedRepo in _suggestedRepositories)
        {
            Contents.Add(new($"  {suggestedRepo}", true));
        }

        Contents.Add(Line.Empty);
        Contents.Add(new("Possible update frequencies", true));

        foreach (string frequency in _availableUpdateFrequencies)
        {
            Contents.Add(new($"  {frequency}", true));
        }

        Contents.Add(Line.Empty);
        Contents.Add(new("Suggested Channels:", true));
        Contents.Add(new($"  {string.Join(", ", _suggestedChannels)}", true));

    }

    protected virtual async Task<int> ParseAndValidateData(TData outputYamlData)
    {
        _data.MergePrs = ParseSetting(outputYamlData.MergePrs, _data.MergePrs, false);
        if (!bool.TryParse(_data.MergePrs, out bool mergePrs))
        {
            _logger.LogError("Merge PRs is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        _data.IgnoredChecks = outputYamlData.IgnoredChecks ?? [];

        _data.Channel = ParseSetting(outputYamlData.Channel, _data.Channel, false);
        if (string.IsNullOrEmpty(_data.Channel))
        {
            _logger.LogError("Channel must be non-empty");
            return Constants.ErrorCode;
        }

        _data.SourceRepository = ParseSetting(outputYamlData.SourceRepository, _data.SourceRepository, false);
        if (string.IsNullOrEmpty(_data.SourceRepository))
        {
            _logger.LogError("Source repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        if (!Uri.TryCreate(_data.SourceRepository, UriKind.Absolute, out Uri? _))
        {
            _logger.LogError("Source repository URL must be a valid URI");
            return Constants.ErrorCode;
        }

        _data.TargetRepository = ParseSetting(outputYamlData.TargetRepository, _data.TargetRepository, false);
        if (string.IsNullOrEmpty(_data.TargetRepository))
        {
            _logger.LogError("Target repository URL must be non-empty");
            return Constants.ErrorCode;
        }

        if (!Uri.TryCreate(_data.TargetRepository, UriKind.Absolute, out Uri? _))
        {
            _logger.LogError("Target repository URL must be a valid URI");
            return Constants.ErrorCode;
        }

        _data.TargetBranch = ParseSetting(outputYamlData.TargetBranch, _data.TargetBranch, false);
        if (string.IsNullOrEmpty(_data.TargetBranch))
        {
            _logger.LogError("Target branch must be non-empty");
            return Constants.ErrorCode;
        }

        _data.Batchable = ParseSetting(outputYamlData.Batchable, _data.Batchable, false);

        if (!bool.TryParse(_data.Batchable, out bool batchable))
        {
            _logger.LogError("Batchable is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        if (batchable && mergePrs)
        {
            _logger.LogError("Batchable subscriptions cannot enable Merge PRs. Configure merging for the repository and branch instead.");
            return Constants.ErrorCode;
        }

        _data.UpdateFrequency = ParseSetting(outputYamlData.UpdateFrequency, _data.UpdateFrequency, false);
        if (string.IsNullOrEmpty(_data.UpdateFrequency) ||
            !Constants.AvailableFrequencies.Contains(_data.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogError($"Frequency should be provided and should be one of the following: " +
                             $"'{string.Join("', '", Constants.AvailableFrequencies)}'");
            return Constants.ErrorCode;
        }

        _data.SourceEnabled = outputYamlData.SourceEnabled;

        if (!bool.TryParse(outputYamlData.SourceEnabled, out bool sourceEnabled))
        {
            _logger.LogError("SourceEnabled is not a valid boolean value.");
            return Constants.ErrorCode;
        }

        _data.AutoApprove = ParseSetting(outputYamlData.AutoApprove, _data.AutoApprove, false);

        if (string.IsNullOrEmpty(_data.AutoApprove))
        {
            _data.AutoApprove = false.ToString();
        }

        if (!bool.TryParse(_data.AutoApprove, out bool _))
        {
            _logger.LogError("AutoApprove is not a valid boolean value.");
            return Constants.ErrorCode;
        }



        if (sourceEnabled)
        {
            if (string.IsNullOrEmpty(outputYamlData.SourceDirectory) && string.IsNullOrEmpty(outputYamlData.TargetDirectory))
            {
                _logger.LogError("Source or target directory must be provided for source-enabled subscriptions");
                return Constants.ErrorCode;
            }

            if (!string.IsNullOrEmpty(outputYamlData.SourceDirectory) && !string.IsNullOrEmpty(outputYamlData.TargetDirectory))
            {
                _logger.LogError("Only one of source or target directory can be provided for source-enabled subscriptions");
                return Constants.ErrorCode;
            }

            // For subscriptions targeting the VMR, we need to ensure that the target is indeed a VMR
            try
            {
                if (!string.IsNullOrEmpty(outputYamlData.TargetDirectory) && !_forceCreation)
                {
                    await CheckIfRepoIsVmr(outputYamlData.TargetRepository, outputYamlData.TargetBranch);
                }

                if (!string.IsNullOrEmpty(outputYamlData.SourceDirectory) && !_forceCreation)
                {
                    await CheckIfRepoIsVmr(outputYamlData.SourceRepository, "main");
                }
            }
            catch (DarcException e)
            {
                _logger.LogError(e.Message);
                return Constants.ErrorCode;
            }
        }

        // When we disable the source flow, we zero out the source directory
        if (!sourceEnabled)
        {
            outputYamlData.SourceDirectory = null;
        }

        _data.FailureNotificationTags = ParseSetting(outputYamlData.FailureNotificationTags, _data.FailureNotificationTags, false);
        _data.SourceDirectory = outputYamlData.SourceDirectory;
        _data.TargetDirectory = outputYamlData.TargetDirectory;
        _data.ExcludedAssets = outputYamlData.ExcludedAssets;

        return Constants.SuccessCode;
    }

    public override async Task<int> ProcessContents(IList<Line> contents)
    {
        TData outputYamlData;

        try
        {
            outputYamlData = ParseYamlData<TData>(contents);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml. Please see help for correct format.");
            return Constants.ErrorCode;
        }

        return await ParseAndValidateData(outputYamlData);
    }

    protected static T ParseYamlData<T>(IList<Line> contents)
    {
        // Join the lines back into a string and deserialize as YAML.
        string yamlString = contents.Aggregate("", (current, line) => $"{current}{Environment.NewLine}{line.Text}");
        IDeserializer serializer = new DeserializerBuilder().Build();
        return serializer.Deserialize<T>(yamlString);
    }

    private async Task CheckIfRepoIsVmr(string repoUri, string branch)
    {
        try
        {
            var gitRepo = _gitRepoFactory.CreateClient(repoUri);
            await gitRepo.GetFileContentsAsync(VmrInfo.DefaultRelativeSourceManifestPath, repoUri, branch);
        }
        catch (DependencyFileNotFoundException e)
        {
            throw new DarcException($"Target repository is not a VMR ({e.Message}). Use -f to override this check.");
        }
    }
}
