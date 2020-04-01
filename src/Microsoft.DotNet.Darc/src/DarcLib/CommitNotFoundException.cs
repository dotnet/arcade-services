// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib
{
    [Serializable]
    public class CommitNotFoundException : Exception
    {
        public CommitNotFoundException()
        {
        }

        public CommitNotFoundException(string message) : base(message)
        {
        }

        public CommitNotFoundException(string message, Exception inner) : base(message, inner)
        {
        }

        protected CommitNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
