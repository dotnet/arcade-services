// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps
{
    public class AzureDevOpsVariable
    {
        public bool IsSecret { get; set; }
        public string Value { get; set; }

        public AzureDevOpsVariable(string value, bool isSecret = false)
        {
            Value = value;
            IsSecret = isSecret;
        }
    }
}
