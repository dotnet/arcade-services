// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Darc.Options
{
    /// <summary>
    /// This attribute indicates that the value of this option should be redacted
    /// from any logging utilities. Either because it contains secrets or because
    /// it's local paths that provide little use on the server, or because it's a value
    /// that is different every invocation, and not interesting for analytics
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RedactFromLoggingAttribute : Attribute
    {
    }
}
