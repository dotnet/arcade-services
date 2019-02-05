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
    public class UpdateSubscriptionPopUp : SubscriptionPopUp
    {
        private readonly ILogger _logger;

        private SubscriptionData _yamlData;

        public string Channel => _yamlData.Channel;

        public string SourceRepository => _yamlData.SourceRepository;

        public bool Batchable => bool.Parse(_yamlData.Batchable);

        public string UpdateFrequency => _yamlData.UpdateFrequency;

        public bool Enabled => bool.Parse(_yamlData.Enabled);

        public List<MergePolicy> MergePolicies => _yamlData.MergePolicies;

        public UpdateSubscriptionPopUp(string path,
                                    ILogger logger,
                                    Subscription subscription,
                                    IEnumerable<string> suggestedChannels,
                                    IEnumerable<string> suggestedRepositories,
                                    IEnumerable<string> availableUpdateFrequencies,
                                    IEnumerable<string> availableMergePolicyHelp)
            : base(path, logger)
        {
            _logger = logger;
            _yamlData = new SubscriptionData
            {
                Channel = GetCurrentSettingForDisplay(subscription.Channel.Name, subscription.Channel.Name, false),
                SourceRepository = GetCurrentSettingForDisplay(subscription.SourceRepository, subscription.SourceRepository, false),
                Batchable = GetCurrentSettingForDisplay(subscription.Policy.Batchable.ToString(), subscription.Policy.Batchable.ToString(), false),
                UpdateFrequency = GetCurrentSettingForDisplay(subscription.Policy.UpdateFrequency, subscription.Policy.UpdateFrequency, false),
                Enabled = GetCurrentSettingForDisplay(subscription.Enabled.ToString(), subscription.Enabled.ToString(), false),
                MergePolicies = new List<MergePolicy>(subscription.Policy.MergePolicies)
            };

            ISerializer serializer = new SerializerBuilder().Build();

            string yaml = serializer.Serialize(_yamlData);

            string[] lines = yaml.Split(Environment.NewLine);

            // Initialize line contents.  Augment the input lines with suggestions and explanation
            Contents = new Collection<Line>(new List<Line>
            {
                new Line($"Use this form to update the values of subscription '{subscription.Id}'.", true),
                new Line()
            });

            foreach (string line in lines)
            {
                Contents.Add(new Line(line));
            }

            // Add helper comments
            Contents.Add(new Line($"Suggested repository URLs for '{SubscriptionData.sourceRepoElement}':", true));

            foreach (string suggestedRepo in suggestedRepositories)
            {
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
                string yamlString = contents.Aggregate<Line, string>("", (current, line) => $"{current}{System.Environment.NewLine}{line.Text}");
                IDeserializer serializer = new DeserializerBuilder().Build();
                outputYamlData = serializer.Deserialize<SubscriptionData>(yamlString);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
                return Constants.ErrorCode;
            }

            // Make sure Batchable and Enabled are valid bools
            if (!bool.TryParse(outputYamlData.Batchable, out bool batchable) || !bool.TryParse(outputYamlData.Batchable, out bool enabled))
            {
                _logger.LogError("Either Batchable or Enabled is not a valid boolean values.");
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
        private class SubscriptionData
        {
            public const string channelElement = "Channel";
            public const string sourceRepoElement = "Source Repository URL";
            public const string batchable = "Is batchable";
            public const string updateFrequencyElement = "Update Frequency";
            public const string mergePolicyElement = "Merge Policies";
            public const string enabled = "Enabled";

            [YamlMember(Alias = channelElement, ApplyNamingConventions = false)]
            public string Channel { get; set; }

            [YamlMember(Alias = sourceRepoElement, ApplyNamingConventions = false)]
            public string SourceRepository { get; set; }

            [YamlMember(Alias = batchable, ApplyNamingConventions = false)]
            public string Batchable { get; set; }

            [YamlMember(Alias = updateFrequencyElement, ApplyNamingConventions = false)]
            public string UpdateFrequency { get; set; }

            [YamlMember(Alias = enabled, ApplyNamingConventions = false)]
            public string Enabled { get; set; }

            [YamlMember(Alias = mergePolicyElement, ApplyNamingConventions = false)]
            public List<MergePolicy> MergePolicies { get; set; }
        }
    }
}
