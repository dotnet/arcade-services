// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Conductor_Api>("conductor.api");

builder.Build().Run();
