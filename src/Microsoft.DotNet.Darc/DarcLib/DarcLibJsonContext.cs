// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Source-generated System.Text.Json metadata for the types DarcLib serializes.
/// Reflection-based serialization cannot run under Native AOT, so every JsonSerializerOptions
/// instance in DarcLib resolves its type information from here.
/// </summary>
[JsonSerializable(typeof(SourceMappingFile))]
[JsonSerializable(typeof(SourceManifestWrapper))]
[JsonSerializable(typeof(CredScanSuppressionFile))]
internal partial class DarcLibJsonContext : JsonSerializerContext;
