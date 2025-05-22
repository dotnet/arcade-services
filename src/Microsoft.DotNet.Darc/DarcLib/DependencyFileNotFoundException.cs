// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib;

[Serializable]
public class DependencyFileNotFoundException : DarcException
{
    public string File { get; set; }

    public DependencyFileNotFoundException(string filePath, string repository, string branch, Exception innerException)
        : base($"Required dependency file '{filePath}' in repository '{repository}' branch '{branch}' was not found.", innerException)
    {
        File = filePath;
    }

    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
    protected DependencyFileNotFoundException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        File = info.GetString("File");
    }

    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("File", File);
        base.GetObjectData(info, context);
    }

    public DependencyFileNotFoundException()
    {
    }

    public DependencyFileNotFoundException(string message) : base(message)
    {
    }

    public DependencyFileNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
