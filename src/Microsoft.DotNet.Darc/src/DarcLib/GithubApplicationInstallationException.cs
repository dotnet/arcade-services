// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib
{
    [Serializable]
    public class GithubApplicationInstallationException : DarcException
    {
        public GithubApplicationInstallationException() : base()
        {
        }

        protected GithubApplicationInstallationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public GithubApplicationInstallationException(string message) : base(message)
        {
        }

        public GithubApplicationInstallationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
