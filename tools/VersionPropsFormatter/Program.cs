// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

IServiceCollection serviceCollection = new ServiceCollection();

VersionPropsFormatter.VersionPropsFormatter.RegisterServices(serviceCollection);

using var serviceProvider = serviceCollection.BuildServiceProvider();

await ActivatorUtilities.CreateInstance<VersionPropsFormatter.VersionPropsFormatter>(serviceProvider).RunAsync(Directory.GetCurrentDirectory());

