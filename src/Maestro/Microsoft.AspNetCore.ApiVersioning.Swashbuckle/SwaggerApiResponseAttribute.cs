using System;
using System.Net;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.AspNetCore.ApiVersioning.Swashbuckle
{
    /// <summary>
    ///   Implementation of <see cref="SwaggerResponseAttribute"/> that is not inherited.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class SwaggerApiResponseAttribute : SwaggerResponseAttribute
    {
        public SwaggerApiResponseAttribute(int statusCode, string description = null, Type type = null) : base(statusCode, description, type)
        {
        }

        public SwaggerApiResponseAttribute(HttpStatusCode statusCode, string description = null, Type type = null) :
            this((int) statusCode, description, type)
        {
        }
    }
}
