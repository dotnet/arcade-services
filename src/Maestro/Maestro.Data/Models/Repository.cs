// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.DotNet.Services.Utility;
using Newtonsoft.Json;

namespace Maestro.Data.Models;

public class Repository
{
    // 450 is short enough to work well in SQL indexes,
    // and long enough to hold any repository or branch that we need to store.
    public const int RepositoryNameLength = 450;
    public const int BranchNameLength = 450;

    [MaxLength(RepositoryNameLength)]
    public string RepositoryName { get; set; }

    public long InstallationId { get; set; }

    public List<RepositoryBranch> Branches { get; set; }
}

public class RepositoryBranch
{
    [MaxLength(Repository.RepositoryNameLength)]
    public string RepositoryName { get; set; }

    public Repository Repository { get; set; }

    [MaxLength(Repository.BranchNameLength)]
    public string BranchName { get; set; }

    [Column("Policy")]
    public string PolicyString { get; set; }

    [NotMapped]
    public Policy PolicyObject
    {
        get => PolicyString == null ? null : JsonConvert.DeserializeObject<Policy>(PolicyString);
        set => PolicyString = value == null ? null : JsonConvert.SerializeObject(value);
    }

    public Namespace Namespace { get; set; }

    public class Policy
    {
        public List<MergePolicyDefinition> MergePolicies { get; set; }
    }
}
