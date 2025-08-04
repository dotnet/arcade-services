// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

IServiceCollection serviceCollection = new ServiceCollection();

VersionDetailsPropsFormatter.VersionDetailsPropsFormatter.RegisterServices(serviceCollection);

using var serviceProvider = serviceCollection.BuildServiceProvider();

ActivatorUtilities.CreateInstance<VersionDetailsPropsFormatter.VersionDetailsPropsFormatter>(serviceProvider).Run(Directory.GetCurrentDirectory());

