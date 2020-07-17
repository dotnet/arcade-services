using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib
{
    /// <summary>
    ///  General-purpose exception to ensure reporting for failure authenticate to Azure DevOps, GitHub, or future authentication-requiring access
    ///  NOTE: If the inner exception is not serializable, checks for "is DarcException" will fail as the Exception caught becomes one about 
    ///        not being able to serialize.
    /// </summary>
    [Serializable]
    public class DarcAuthenticationFailureException : DarcException
    {
        public DarcAuthenticationFailureException() : base()
        {
        }

        protected DarcAuthenticationFailureException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public DarcAuthenticationFailureException(string message) : base(message)
        {
        }

        public DarcAuthenticationFailureException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
