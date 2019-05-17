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
    internal class SetRepositoryMergePoliciesPopUp : EditorPopUp
    {
        private readonly ILogger _logger;
        private RepositoryPoliciesData _yamlData;
        public string Repository => _yamlData.Repository;
        public string Branch => _yamlData.Branch;
        public List<MergePolicy> MergePolicies => MergePoliciesPopUpHelpers.ConvertMergePolicies(_yamlData.MergePolicies);

        public SetRepositoryMergePoliciesPopUp(string path,
                                              ILogger logger,
                                              string repository,
                                              string branch,
                                              List<MergePolicy> mergePolicies,
                                              IEnumerable<string> availableMergePolicyHelp)
            : base(path)
        {
            _logger = logger;
            _yamlData = new RepositoryPoliciesData
            {
                Repository = GetCurrentSettingForDisplay(repository, "<required>", false),
                Branch = GetCurrentSettingForDisplay(branch, "<required>", false),
            };
            _yamlData.MergePolicies = MergePoliciesPopUpHelpers.ConvertMergePolicies(mergePolicies);

            ISerializer serializer = new SerializerBuilder().Build();
            string yaml = serializer.Serialize(_yamlData);
            string[] lines = yaml.Split(Environment.NewLine);

            // Initialize line contents.  Augment the input lines with suggestions and explanation
            Contents = new Collection<Line>(new List<Line>
            {
                new Line("Use this form to set repository auto-merge policies for batchable subscriptions.", true),
                new Line("Batchable subscriptions share merge policies for all subscriptions that target the same repo and branch.", true),
                new Line("If the branch has at least one merge policy and a PR satisfies that merge policy, the PR is automatically merged.", true),
                new Line("", true),
                new Line("Fill out the following form. Suggested values for merge policies are shown below.", true),
                new Line()
            });
            foreach (string line in lines)
            {
                Contents.Add(new Line(line));
            }
            // Add helper comments
            Contents.Add(new Line("Available Merge Policies", true));
            foreach (string mergeHelp in availableMergePolicyHelp)
            {
                Contents.Add(new Line($"  {mergeHelp}", true));
            }
        }

        public override int ProcessContents(IList<Line> contents)
        {
            RepositoryPoliciesData outputYamlData;

            try
            {
                // Join the lines back into a string and deserialize as YAML.
                string yamlString = contents.Aggregate<Line, string>("", (current, line) => $"{current}{System.Environment.NewLine}{line.Text}");
                IDeserializer serializer = new DeserializerBuilder().Build();
                outputYamlData = serializer.Deserialize<RepositoryPoliciesData>(yamlString);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
                return Constants.ErrorCode;
            }

            // Validate the merge policies
            if (!MergePoliciesPopUpHelpers.ValidateMergePolicies(MergePoliciesPopUpHelpers.ConvertMergePolicies(outputYamlData.MergePolicies), _logger))
            {
                return Constants.ErrorCode;
            }

            _yamlData.MergePolicies = outputYamlData.MergePolicies;

            _yamlData.Repository = ParseSetting(outputYamlData.Repository, _yamlData.Repository, false);
            if (string.IsNullOrEmpty(_yamlData.Repository))
            {
                _logger.LogError("Repository URL must be non-empty");
                return Constants.ErrorCode;
            }

            _yamlData.Branch = ParseSetting(outputYamlData.Branch, _yamlData.Branch, false);
            if (string.IsNullOrEmpty(_yamlData.Branch))
            {
                _logger.LogError("Branch must be non-empty");
                return Constants.ErrorCode;
            }

            return Constants.SuccessCode;
        }

        class RepositoryPoliciesData
        {
            public const string repoElement = "Repository URL";
            public const string branchElement = "Branch";
            public const string mergePolicyElement = "Merge Policies";

            [YamlMember(Alias = branchElement, ApplyNamingConventions = false)]
            public string Branch { get; set; }

            [YamlMember(Alias = repoElement, ApplyNamingConventions = false)]
            public string Repository { get; set; }

            [YamlMember(Alias = mergePolicyElement, ApplyNamingConventions = false)]
            public List<MergePolicyData> MergePolicies { get; set; }
        }
    }
}
