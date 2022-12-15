// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public record Dependency(string Name, LocalPath Uri);
