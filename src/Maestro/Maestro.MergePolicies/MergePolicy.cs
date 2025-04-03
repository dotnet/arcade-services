// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Newtonsoft.Json.Linq;

namespace Maestro.MergePolicies;

public class MergePolicyProperties
{
    public MergePolicyProperties(IReadOnlyDictionary<string, JToken> properties)
    {
        Properties = properties;
    }

    public IReadOnlyDictionary<string, JToken> Properties { get; }

    public T Get<T>(string key)
    {
        T result = default;
        if (Properties != null && Properties.TryGetValue(key, out JToken value))
        {
            result = value.ToObject<T>();
        }

        return result;
    }
}

public abstract class MergePolicy : IMergePolicy
{
    protected static readonly string configurationErrorsHeader = """
         ### :x: Check Failed

         The following error(s) were encountered:


        """;

    protected static readonly string codeFlowCheckSeekHelpMsg = $"""


        ### :exclamation: IMPORTANT

        The `{VmrInfo.DefaultRelativeSourceManifestPath}` and `{VersionFiles.VersionDetailsXml}` files are managed by Maestro/darc. Outside of exceptional circumstances, these files should not be modified manually.
        **Unless you are sure that you know what you are doing, we recommend reaching out for help**. You can receive assistance by:
        - tagging the **@dotnet/product-construction** team in a PR comment
        - using the [First Responder channel](https://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606dhttps://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606d),
        - [opening an issue](https://github.com/dotnet/arcade-services/issues/new?template=BLANK_ISSUE) in the dotnet/arcade-services repo
        - contacting the [.NET Product Construction Services team via e-mail](mailto:dotnetprodconsvcs@microsoft.com).
        """;

    public string Name
    {
        get
        {
            string name = GetType().Name;
            if (name.EndsWith(nameof(MergePolicy)))
            {
                name = name.Substring(0, name.Length - nameof(MergePolicy).Length);
            }

            return name;
        }
    }

    public abstract string DisplayName { get; }

    public abstract Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);

    public MergePolicyEvaluationResult Pending(string title) => new(MergePolicyEvaluationStatus.Pending, title, string.Empty, this);

    public MergePolicyEvaluationResult Succeed(string title) => new(MergePolicyEvaluationStatus.Success, title, string.Empty, this);

    public MergePolicyEvaluationResult Fail(string title) => new(MergePolicyEvaluationStatus.Failure, title, string.Empty, this);

    public MergePolicyEvaluationResult Fail(string title, string message) => new(MergePolicyEvaluationStatus.Failure, title, message, this);
}

public interface IMergePolicy : IMergePolicyInfo
{
    Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);
}

public interface IMergePolicyBuilder
{
    string Name { get; }

    /// <summary>
    /// Creates list of instances of concrete merge policies which shall be evaluated 
    /// for particular merge policy definition
    /// In most cases it will return array of exactly one merge policy, but in special cases like standard-policy
    /// it will return multiple pre-configured policies which that policies template consist of
    /// </summary>
    Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr);
}
