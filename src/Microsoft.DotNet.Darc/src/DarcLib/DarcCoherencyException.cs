// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib
{
    public class CoherencyError
    {
        public DependencyDetail Dependency { get; set; }
        public string Error { get; set; }
        public IEnumerable<string> PotentialSolutions { get; set; }
    }

    [Serializable]
    public class DarcCoherencyException : DarcException
    {
        public IEnumerable<CoherencyError> Errors { get; private set; }

        public DarcCoherencyException(IEnumerable<CoherencyError> coherencyErrors)
             : base("Coherency update failed for the following dependencies: " +
                    string.Join(", ", coherencyErrors.Select(error => error.Dependency.Name)))
        {
            Errors = coherencyErrors;
        }

        public DarcCoherencyException(CoherencyError coherencyError)
            : this(new List<CoherencyError> { coherencyError })
        {
        }
    }
}
