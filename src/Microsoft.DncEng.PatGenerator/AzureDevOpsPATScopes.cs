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
    public class ScopeDescriptionAttribute : Attribute
    {
        public string Resource { get; }
        public string Permissions { get; }
        public string Description { get; set; }

        public ScopeDescriptionAttribute(string category, string shortName, string description)
        {
            Resource = category;
            Permissions = shortName;
            Description = description;
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
    /// 
    /// Note that this is not the full set. Additional scopes can be added if necessary.
    /// </summary>
    [Flags]
    public enum AzureDevOpsPATScopes
    {
        // Build - Bits 0x3

        [ScopeDescription("build", "r", "Build (read)")]
        build = 0x1,
        [ScopeDescription("build", "re", "Build (read and execute)")]
        build_execute = 0x2 | build,

        // Code - Bits 0x3C

        [ScopeDescription("code", "r", "Code (read)")]
        code = 0x4,
        [ScopeDescription("code", "s", "Code (update commit status)")]
        code_status = 0x8,
        [ScopeDescription("code", "rw", "Code (read and write)")]
        code_write = 0x10 | code | code_status,
        [ScopeDescription("code", "m", "Code (read, write, and manage)")]
        code_manage = 0x20 | code_write,

        // Packaging - Bits 0x1C0

        [ScopeDescription("package", "r", "Packaging (read)")]
        packaging = 0x40,
        [ScopeDescription("package", "rw", "Packaging (read and write)")]
        packaging_write = 0x80 | packaging,
        [ScopeDescription("package", "m", "Packaging (read, write, and manage)")]
        packaging_manage = 0x100 | packaging_write,

        // Symbols - Bits 0xE00

        [ScopeDescription("symbols", "r", "Symbols (read)")]
        symbols = 0x200,
        [ScopeDescription("symbols", "rw", "Symbols (read and write)")]
        symbols_write = 0x400 | symbols,
        [ScopeDescription("symbols", "m", "Symbols (read, write and manage)")]
        symbols_manage = 0x800 | symbols_write,

        // Release - Bits 0x7000

        [ScopeDescription("release", "r", "Release (read)")]
        release = 0x1000,
        [ScopeDescription("release", "rw", "Release (read, write and execute)")]
        release_execute = 0x2000 | release,
        [ScopeDescription("release", "m", "Release (read, write, execute and manage)")]
        release_manage = 0x4000 | release_execute,

        // User Profile - Bits 0x18000 - Note that write does not appear to include read

        [ScopeDescription("profile", "r", "User profile (read)")]
        profile = 0x8000,
        [ScopeDescription("profile", "w", "User profile (write)")]
        profile_write = 0x10000,

        // Variable Groups - Bits 0xE0000

        [ScopeDescription("variablegroups", "r", "Variable Groups (read)")]
        variablegroups_read = 0x20000,
        [ScopeDescription("variablegroups", "rw", "Variable Groups (read, create)")]
        variablegroups_write = 0x40000 | variablegroups_read,
        [ScopeDescription("variablegroups", "m", "Variable Groups (read, create and manage)")]
        variablegroups_manage = 0x80000 | variablegroups_write,

        // Work items - Bits 0x700000

        [ScopeDescription("work", "r", "Work items (read)")]
        work = 0x100000,
        [ScopeDescription("work", "rw", "Work items (read and write)")]
        work_write = 0x200000 | work,
        [ScopeDescription("work", "f", "Work items (full)")]
        work_full = 0x400000 | work_write,
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
            var shortHandType = typeof(ScopeDescriptionAttribute);

            Dictionary<string, string> minimalScopesStrings = new Dictionary<string, string>();

            // Build up a dictionary of strings with common category roots.
            foreach (var scope in minimalScopes)
            {
                var memberInfo = azdoScopeType.GetMember(scope.ToString());
                var attribute = Attribute.GetCustomAttribute(memberInfo[0], shortHandType);
                if (attribute == null)
                {
                    throw new Exception($"{scope.ToString()} should have a 'ScopeShortHand' attribute.");
                }

                var shortHandTypeAttribute = (ScopeDescriptionAttribute)attribute;

                if (minimalScopesStrings.ContainsKey(shortHandTypeAttribute.Resource))
                {
                    minimalScopesStrings[shortHandTypeAttribute.Resource] += shortHandTypeAttribute.Permissions;
                }
                else
                {
                    minimalScopesStrings.Add(shortHandTypeAttribute.Resource, shortHandTypeAttribute.Permissions);
                }
            }

            // Join their values together.
            return string.Join('-', minimalScopesStrings.Select(kv => $"{kv.Key}-{kv.Value}"));
        }
    }
}
