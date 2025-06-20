// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.PopUps;

/// <summary>
/// Common class for subscription management popups.
/// </summary>
internal abstract class SubscriptionPopUp<TData> : EditorPopUp where TData : SubscriptionData
{
    protected readonly TData _data;
    private readonly bool _forceCreation;
    private readonly IEnumerable<string> _suggestedChannels;
    private readonly IEnumerable<string> _suggestedRepositories;
    private readonly IEnumerable<string> _availableUpdateFrequencies;
    private readonly IEnumerable<string> _availableMergePolicyHelp;
    private readonly ILogger _logger;
    private readonly IGitRepoFactory _gitRepoFactory;

    public string Channel => _data.Channel;
    public string SourceRepository => _data.SourceRepository;
    public string TargetRepository => _data.TargetRepository;
    public string TargetBranch => _data.TargetBranch;
    public string UpdateFrequency => _data.UpdateFrequency;
    public List<MergePolicy> MergePolicies => MergePoliciesPopUpHelpers.ConvertMergePolicies(_data.MergePolicies);
    public bool Batchable => bool.Parse(_data.Batchable);
    public string? FailureNotificationTags => _data.FailureNotificationTags;
    public bool SourceEnabled => bool.Parse(_data.SourceEnabled);
    public IReadOnlyCollection<string> ExcludedAssets => _data.ExcludedAssets;
    public string? SourceDirectory => _data.SourceDirectory;
    public string? TargetDirectory => _data.TargetDirectory;

    protected SubscriptionPopUp(
        string path,
        bool forceCreation,
        IEnumerable<string> suggestedChannels,
        IEnumerable<string> suggestedRepositories,
        IEnumerable<string> availableUpdateFrequencies,
        IEnumerable<string> availableMergePolicyHelp,
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
        _availableMergePolicyHelp = availableMergePolicyHelp;
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
            if (line.StartsWith(SubscriptionData.SourceEnabledElement))
            {
                Contents.AddRange(
                [
                    new(),
                    new("Properties for code-enabled subscriptions (VMR code flow related):", true),
                ]);
            }

            Contents.Add(new Line(line));
        }

        Contents.Add(new($"Suggested repository URLs for '{SubscriptionData.SourceRepoElement}' or '{SubscriptionData.TargetRepoElement}':", true));

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

        Contents.Add(Line.Empty);
        Contents.Add(new("Available Merge Policies", true));

        foreach (string mergeHelp in _availableMergePolicyHelp)
        {
            Contents.Add(new($"  {mergeHelp}", true));
        }
    }

    protected virtual async Task<int> ParseAndValidateData(TData outputYamlData)
    {
        if (!MergePoliciesPopUpHelpers.ValidateMergePolicies(MergePoliciesPopUpHelpers.ConvertMergePolicies(outputYamlData.MergePolicies), _logger))
        {
            return Constants.ErrorCode;
        }

        _data.MergePolicies = outputYamlData.MergePolicies;

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

        if (!bool.TryParse(outputYamlData.Batchable, out bool _))
        {
            _logger.LogError("Batchable is not a valid boolean value.");
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

        // When we disable the source flow, we zero out the source/target directory
        if (!sourceEnabled)
        {
            outputYamlData.SourceDirectory = null;
            outputYamlData.TargetDirectory = null;
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
