// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Maestro.Contracts;

[DataContract]
public class CoherencyErrorDetails
{
    [DataMember]
    public string Error { get; set; }

    [DataMember]
    public IEnumerable<string> PotentialSolutions { get; set; }
}
