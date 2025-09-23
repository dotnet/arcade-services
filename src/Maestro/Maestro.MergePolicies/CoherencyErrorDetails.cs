// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Maestro.MergePolicies;

public class CoherencyErrorDetails
{
    public string Error { get; set; }

    public IEnumerable<string> PotentialSolutions { get; set; }
}
