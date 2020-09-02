using System;

namespace Microsoft.AspNetCore.ApiVersioning
{
    /// <summary>
    ///   This attribute marks the attributed type as "versioned" so the roslyn analyzer won't warn about it.
    ///   This should only be used on types that never change and are shared by all api versions, like error models.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SuppressVersionErrorsAttribute : Attribute
    {
    }
}
