// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.ProductConstructionService.Client
{
    [Serializable]
    public class AuthenticationException : Exception
    {
        public AuthenticationException()
        {
        }

        [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
        protected AuthenticationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public AuthenticationException(string message) : base(message)
        {
        }

        public AuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
