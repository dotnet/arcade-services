// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Contracts;
using Microsoft.DotNet.DarcLib;
using Newtonsoft.Json.Linq;

namespace Maestro.MergePolicies
{
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

        public abstract Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc);

        public MergePolicyEvaluationResult Pending(string message) => new MergePolicyEvaluationResult(MergePolicyEvaluationStatus.Pending, message, this);

        public MergePolicyEvaluationResult Succeed(string message) => new MergePolicyEvaluationResult(MergePolicyEvaluationStatus.Success, message, this);

        public MergePolicyEvaluationResult Fail(string message) => new MergePolicyEvaluationResult(MergePolicyEvaluationStatus.Failure, message, this);
    }

    public interface IMergePolicy : IMergePolicyInfo
    {
        Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc);
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
        Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr);
    }
}
