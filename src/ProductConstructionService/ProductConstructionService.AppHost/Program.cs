// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

var builder = DistributedApplication.CreateBuilder(args);

var queues = builder.AddAzureStorage("storage")
    .UseEmulator()
    .AddQueues("queues");


//builder.AddProject<Projects.ProductConstructionService_Api>("productConstructionService.api")
//    .WithReference(queues);

builder.Build().Run();
