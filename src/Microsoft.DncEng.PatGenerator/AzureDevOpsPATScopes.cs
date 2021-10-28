// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.DncEng.PatGenerator
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ScopeShortHandAttribute : Attribute
    {
        public string Category { get; }
        public string Permissions { get; }

        public ScopeShortHandAttribute(string category, string shortName)
        {
            Category = category;
            Permissions = shortName;
        }
    }

    /// <summary>
    /// Available Azure DevOps Scopes
    /// See <see cref="https://docs.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/oauth?view=azure-devops#scopes"/>
    /// for more information.
    /// 
    /// A flags enumeration is used to coalesce different permissions levels. For example,
    /// if 'build' and 'build_execute' were supplied together, 'build' is not useful since it's
    /// implied in build_execute.
    /// </summary>
    [Flags]
    public enum AzureDevOpsPATScopes
    {
        // Build - Bits 0x3

        [ScopeShortHand("build", "r")]
        build = 0x1,
        [ScopeShortHand("build", "re")]
        build_execute = 0x2 | build,

        // Code - Bits 0x3C

        [ScopeShortHand("code", "r")]
        code = 0x4,
        [ScopeShortHand("code", "s")]
        code_status = 0x8,
        [ScopeShortHand("code", "rw")]
        code_write = 0x10 | code | code_status,
        [ScopeShortHand("code", "m")]
        code_manage = 0x20 | code_write,

        // Packaging - Bits 0x1C0

        [ScopeShortHand("package", "r")]
        packaging = 0x40,
        [ScopeShortHand("package", "rw")]
        packaging_write = 0x80 | packaging,
        [ScopeShortHand("package", "m")]
        packaging_manage = 0x100 | packaging_write,

        // Symbols - Bits 0xE00

        [ScopeShortHand("symbols", "r")]
        symbols = 0x200,
        [ScopeShortHand("symbols", "rw")]
        symbols_write = 0x400 | symbols,
        [ScopeShortHand("symbols", "m")]
        symbols_manage = 0x800 | symbols_write,
    }

    public static class AzureDevOpsPATScopesExtensions
    {
        /// <summary>
        /// Pull out a minimal
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static List<AzureDevOpsPATScopes> GetMinimizedScopeList(this AzureDevOpsPATScopes e)
        {
            // Note that in net5.0+ there is a generic version of this, but
            // it's not available in 3.1.
            // Get the list of available values, which are automatically sorted by their magnitude.
            var availableValues = (int[])Enum.GetValues(typeof(AzureDevOpsPATScopes));

            // To figure out a minimal set, rely on the flags setup that means that more permissive scopes
            // scopes include the the less permissive ones.
            //
            // We walk the list of avaiable values, checking whether the bit is set. If it is, check whether
            // any existing scopes in the list are contained within this scope. If they are, remove them.
            // Then add the new scope to the list.
            List<AzureDevOpsPATScopes> minimalScopes = new List<AzureDevOpsPATScopes>();
            
            foreach (var value in availableValues)
            {
                AzureDevOpsPATScopes flag = (AzureDevOpsPATScopes)value;
                if (e.HasFlag(flag))
                {
                    // If this scope includes any of the existing minimal scopes (X & Y != 0) then
                    // remove those existing minimal scopes.
                    var existingMatchingMinimalScopes = minimalScopes.Where(ms => (flag & ms) != 0).ToList();
                    if (existingMatchingMinimalScopes.Any())
                    {
                        existingMatchingMinimalScopes.ForEach(ms => minimalScopes.Remove(ms));
                    }
                    minimalScopes.Add(flag);
                }
            }

            return minimalScopes;
        }

        /// <summary>
        /// Get a scope description string.
        ///
        /// Group the categories together so as to generate the most minimal string. Some of this is done via
        /// the flags enum minimization step, but there are some disjoint roots that require an additional step.
        /// <param name="scopes">Scopes</param>
        /// <returns>Description string.</returns>
        /// <exception cref="Exception">Exception if an attribute is missing from the enum.</exception>
        public static string GetScopeString(this AzureDevOpsPATScopes scopes)
        {
            var minimalScopes = scopes.GetMinimizedScopeList();

            var azdoScopeType = typeof(AzureDevOpsPATScopes);
            var shortHandType = typeof(ScopeShortHandAttribute);

            Dictionary<string, string> minimalScopesStrings = new Dictionary<string, string>();

            // Build up a dictionary of strings with common category roots.
            foreach (var scope in minimalScopes)
            {
                var memberInfo = azdoScopeType.GetMember(scope.ToString());
                var attributes = memberInfo[0].GetCustomAttributes(shortHandType, false);
                if (attributes.Length == 0)
                {
                    throw new Exception($"{scope.ToString()} should have a 'ScopeShortHand' attribute.");
                }

                var attribute = (ScopeShortHandAttribute)attributes[0];
                if (minimalScopesStrings.ContainsKey(attribute.Category))
                {
                    minimalScopesStrings[attribute.Category] += attribute.Permissions;
                }
                else
                {
                    minimalScopesStrings.Add(attribute.Category, attribute.Permissions);
                }
            }

            // Join their values together.
            return string.Join('-', minimalScopesStrings.Select(kv => $"{kv.Key}-{kv.Value}"));
        }
    }
}
