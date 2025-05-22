// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// This file contains suppressions for warnings related to .NET 8.0 serialization API obsolescence
// These suppressions are necessary because the serialization APIs are marked obsolete in .NET 8
// but are still required for backward compatibility

using System.Diagnostics.CodeAnalysis;

// Suppress warning about using obsolete serialization constructor
[assembly: SuppressMessage("Usage", "CS0618:Type or member is obsolete", 
    Justification = "Serialization constructors are required for backward compatibility", 
    Scope = "namespaceanddescendants", 
    Target = "Microsoft.DotNet.DarcLib")]
