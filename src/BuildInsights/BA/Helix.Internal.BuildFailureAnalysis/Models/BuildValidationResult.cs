// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class BuildValidationResult
    {
        public BuildValidationStatus Result { get; }
        public string Message { get; }

        public BuildValidationResult(BuildValidationStatus result, string message)
        {
            Result = result;
            Message = message;
        }
    }
}
