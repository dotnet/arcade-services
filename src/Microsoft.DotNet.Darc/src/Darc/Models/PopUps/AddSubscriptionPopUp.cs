// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps
{
    public class AddSubscriptionPopUp : SubscriptionPopUp
    {
        private readonly ILogger _logger;
        private SubscriptionData _yamlData;
        public string Channel => _yamlData.Channel;
        public string SourceRepository => _yamlData.SourceRepository;
        public string TargetRepository => _yamlData.TargetRepository;
        public string TargetBranch => _yamlData.TargetBranch;
        public string UpdateFrequency => _yamlData.UpdateFrequency;
        public List<MergePolicy> MergePolicies => _yamlData.MergePolicies;

        public AddSubscriptionPopUp(string path,
                                    ILogger logger,
                                    string channel,
                                    string sourceRepository,
                                    string targetRepository,
                                    string targetBranch,
                                    string updateFrequency,
                                    List<MergePolicy> mergePolicies,
                                    IEnumerable<string> suggestedChannels,
                                    IEnumerable<string> suggestedRepositories,
                                    IEnumerable<string> availableUpdateFrequencies,
                                    IEnumerable<string> availableMergePolicyHelp)
            : base(path, logger)
        {
            _logger = logger;
            _yamlData = new SubscriptionData
            {
                Channel = GetCurrentSettingForDisplay(channel, "<required>", false),
                SourceRepository = GetCurrentSettingForDisplay(sourceRepository, "<required>", false),
                TargetRepository = GetCurrentSettingForDisplay(targetRepository, "<required>", false),
                TargetBranch = GetCurrentSettingForDisplay(targetBranch, "<required>", false),
                UpdateFrequency = GetCurrentSettingForDisplay(updateFrequency, $"<'{string.Join("', '", Constants.AvailableFrequencies)}'>", false),
                MergePolicies = new List<MergePolicy>(mergePolicies)
            };

            ISerializer serializer = new SerializerBuilder().Build();
            string yaml = serializer.Serialize(_yamlData);
            string[] lines = yaml.Split(Environment.NewLine);

            // Initialize line contents.  Augment the input lines with suggestions and explanation
            Contents = new Collection<Line>(new List<Line>
            {
                new Line("Use this form to create a new subscription.", true),
                new Line("A subscription maps a build of a source repository that has been applied to a specific channel", true),
                new Line("onto a specific branch in a target repository.  The subscription has a trigger (update frequency)", true),
                new Line("and merge policy.  For additional information about subscriptions, please see", true),
                new Line("https://github.com/dotnet/arcade/blob/master/Documentation/BranchesChannelsAndSubscriptions.md", true),
                new Line("", true),
                new Line("Fill out the following form.  Suggested values for fields are shown below.", true),
                new Line()
            });
            foreach (string line in lines)
            {
                Contents.Add(new Line(line));
            }
            // Add helper comments
            Contents.Add(new Line($"Suggested repository URLs for '{SubscriptionData.sourceRepoElement}' or '{SubscriptionData.targetRepoElement}':", true));
            foreach (string suggestedRepo in suggestedRepositories) {
                Contents.Add(new Line($"  {suggestedRepo}", true));
            }
            Contents.Add(new Line("", true));
            Contents.Add(new Line("Suggested Channels", true));
            foreach (string suggestedChannel in suggestedChannels)
            {
                Contents.Add(new Line($"  {suggestedChannel}", true));
            }
            Contents.Add(new Line("", true));
            Contents.Add(new Line("Available Merge Policies", true));
            foreach (string mergeHelp in availableMergePolicyHelp)
            {
                Contents.Add(new Line($"  {mergeHelp}", true));
            }
        }

        public override int ProcessContents(IList<Line> contents)
        {
            SubscriptionData outputYamlData;

            try
            {
                // Join the lines back into a string and deserialize as YAML.
                // TODO: Alter the popup/ux manager to pass along the raw file to avoid this unnecessary
                // operation once authenticate ends up as YAML.
                string yamlString = contents.Aggregate<Line, string>("", (current, line) => $"{current}{System.Environment.NewLine}{line.Text}");
                IDeserializer serializer = new DeserializerBuilder().Build();
                outputYamlData = serializer.Deserialize<SubscriptionData>(yamlString);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
                return Constants.ErrorCode;
            }

            // Validate the merge policies
            if (!ValidateMergePolicies(outputYamlData.MergePolicies))
            {
                return Constants.ErrorCode;
            }

            _yamlData.MergePolicies = outputYamlData.MergePolicies;

            // Parse and check the input fields
            _yamlData.Channel = ParseSetting(outputYamlData.Channel, _yamlData.Channel, false);
            if (string.IsNullOrEmpty(_yamlData.Channel))
            {
                _logger.LogError("Channel must be non-empty");
                return Constants.ErrorCode;
            }

            _yamlData.SourceRepository = ParseSetting(outputYamlData.SourceRepository, _yamlData.SourceRepository, false);
            if (string.IsNullOrEmpty(_yamlData.SourceRepository))
            {
                _logger.LogError("Source repository URL must be non-empty");
                return Constants.ErrorCode;
            }

            _yamlData.TargetRepository = ParseSetting(outputYamlData.TargetRepository, _yamlData.TargetRepository, false);
            if (string.IsNullOrEmpty(_yamlData.TargetRepository))
            {
                _logger.LogError("Target repository URL must be non-empty");
                return Constants.ErrorCode;
            }

            _yamlData.TargetBranch = ParseSetting(outputYamlData.TargetBranch, _yamlData.TargetBranch, false);
            if (string.IsNullOrEmpty(_yamlData.TargetBranch))
            {
                _logger.LogError("Target branch must be non-empty");
                return Constants.ErrorCode;
            }

            _yamlData.UpdateFrequency = ParseSetting(outputYamlData.UpdateFrequency, _yamlData.UpdateFrequency, false);
            if (string.IsNullOrEmpty(_yamlData.UpdateFrequency) || 
                !Constants.AvailableFrequencies.Contains(_yamlData.UpdateFrequency, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError($"Frequency should be provided and should be one of the following: " +
                    $"'{string.Join("', '",Constants.AvailableFrequencies)}'");
                return Constants.ErrorCode;
            }

            return Constants.SuccessCode;
        }

        /// <summary>
        /// Helper class for YAML encoding/decoding purposes.
        /// This is used so that we can have friendly alias names for elements.
        /// </summary>
        class SubscriptionData
        {
            public const string channelElement = "Channel";
            public const string sourceRepoElement = "Source Repository URL";
            public const string targetRepoElement = "Target Repository URL";
            public const string targetBranchElement = "Target Branch";
            public const string updateFrequencyElement = "Update Frequency";
            public const string mergePolicyElement = "Merge Policies";

            [YamlMember(Alias = channelElement, ApplyNamingConventions = false)]
            public string Channel { get; set; }
            [YamlMember(Alias = sourceRepoElement, ApplyNamingConventions = false)]
            public string SourceRepository { get; set; }
            [YamlMember(Alias = targetRepoElement, ApplyNamingConventions = false)]
            public string TargetRepository { get; set; }
            [YamlMember(Alias = targetBranchElement, ApplyNamingConventions = false)]
            public string TargetBranch { get; set; }
            [YamlMember(Alias = updateFrequencyElement, ApplyNamingConventions = false)]
            public string UpdateFrequency { get; set; }
            [YamlMember(Alias = mergePolicyElement, ApplyNamingConventions = false)]
            public List<MergePolicy> MergePolicies { get; set; }
        }
    }
}
