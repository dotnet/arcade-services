// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    [Serializable]
    public class RepositoryMarkedCriticalMoreThanOnceException : Exception
    {
        public Dictionary<SourceBuildIdentity, SourceBuildEdge[]> IdentityConflictingEdges { get; set; }

        public RepositoryMarkedCriticalMoreThanOnceException()
        {
        }

        public RepositoryMarkedCriticalMoreThanOnceException(string message) : base(message)
        {
        }

        public RepositoryMarkedCriticalMoreThanOnceException(string message, Exception inner) : base(message, inner)
        {
        }

        protected RepositoryMarkedCriticalMoreThanOnceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
