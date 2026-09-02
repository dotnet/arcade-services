// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.Darc.Models.PopUps;

internal class SetRepositoryMergePoliciesPopUp : EditorPopUp
{
    private readonly ILogger _logger;
    private readonly RepositoryPoliciesData _yamlData;
    public string Repository => _yamlData.Repository;
    public string Branch => _yamlData.Branch;
    public bool MergePrs => bool.Parse(_yamlData.MergePrs);
    public IReadOnlyCollection<string> IgnoredChecks => _yamlData.IgnoredChecks;

    public SetRepositoryMergePoliciesPopUp(string path,
        ILogger logger,
        string repository,
        string branch,
        bool mergePrs,
        IReadOnlyCollection<string> ignoredChecks)
        : base(path)
    {
        _logger = logger;
        _yamlData = new RepositoryPoliciesData
        {
            Repository = GetCurrentSettingForDisplay(repository, "<required>", false),
            Branch = GetCurrentSettingForDisplay(branch, "<required>", false),
            MergePrs = GetCurrentSettingForDisplay(mergePrs.ToString(), mergePrs.ToString(), false),
            IgnoredChecks = ignoredChecks
        };

        ISerializer serializer = new SerializerBuilder().Build();
        string yaml = serializer.Serialize(_yamlData);
        string[] lines = yaml.Split(Environment.NewLine);

        // Initialize line contents.  Augment the input lines with suggestions and explanation
        Contents =
        [
            new("Use this form to configure Merge PRs for batchable subscriptions.", true),
            new("Batchable subscriptions share merge settings for all subscriptions that target the same repo and branch.", true),
            Line.Empty,
            new("Fill out the following form.", true),
            new()
        ];

        foreach (string line in lines)
        {
            Contents.Add(new Line(line));
        }

    }

    public override Task<int> ProcessContents(IList<Line> contents)
    {
        RepositoryPoliciesData outputYamlData;

        try
        {
            // Join the lines back into a string and deserialize as YAML.
            string yamlString = string.Join(Environment.NewLine, contents.Select(line => line.Text));
            IDeserializer serializer = new DeserializerBuilder().Build();
            outputYamlData = serializer.Deserialize<RepositoryPoliciesData>(yamlString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to parse input yaml.  Please see help for correct format.");
            return Task.FromResult(Constants.ErrorCode);
        }

        _yamlData.MergePrs = ParseSetting(outputYamlData.MergePrs, _yamlData.MergePrs, false);
        if (!bool.TryParse(_yamlData.MergePrs, out bool mergePrs))
        {
            _logger.LogError("Merge PRs is not a valid boolean value.");
            return Task.FromResult(Constants.ErrorCode);
        }

        _yamlData.IgnoredChecks = outputYamlData.IgnoredChecks ?? [];
        if (!mergePrs && _yamlData.IgnoredChecks.Count != 0)
        {
            _logger.LogError("Ignored Checks can only be specified when Merge PRs is enabled.");
            return Task.FromResult(Constants.ErrorCode);
        }

        _yamlData.Repository = ParseSetting(outputYamlData.Repository, _yamlData.Repository, false);
        if (string.IsNullOrEmpty(_yamlData.Repository))
        {
            _logger.LogError("Repository URL must be non-empty");
            return Task.FromResult(Constants.ErrorCode);
        }

        _yamlData.Branch = ParseSetting(outputYamlData.Branch, _yamlData.Branch, false);
        if (string.IsNullOrEmpty(_yamlData.Branch))
        {
            _logger.LogError("Branch must be non-empty");
            return Task.FromResult(Constants.ErrorCode);
        }

        return Task.FromResult(Constants.SuccessCode);
    }

    private class RepositoryPoliciesData
    {
        public const string RepoElement = "Repository URL";
        public const string BranchElement = "Branch";
        public const string MergePrsElement = "Merge PRs";
        public const string IgnoredChecksElement = "Ignored Checks";

        [YamlMember(Alias = BranchElement, ApplyNamingConventions = false)]
        public string Branch { get; set; }

        [YamlMember(Alias = RepoElement, ApplyNamingConventions = false)]
        public string Repository { get; set; }

        [YamlMember(Alias = MergePrsElement, ApplyNamingConventions = false)]
        public string MergePrs { get; set; }

        [YamlMember(Alias = IgnoredChecksElement, ApplyNamingConventions = false)]
        public IReadOnlyCollection<string> IgnoredChecks { get; set; }
    }
}
