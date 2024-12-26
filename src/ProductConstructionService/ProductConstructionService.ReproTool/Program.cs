// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProductConstructionService.ReproTool;

Parser.Default.ParseArguments<ReproToolOptions>(args)
    .WithParsed<ReproToolOptions>(o =>
    {
        ServiceCollection services = new ServiceCollection();

        services.RegisterServices(o);

        var provider = services.BuildServiceProvider();

        ActivatorUtilities.CreateInstance<ReproTool>(provider).ReproduceCodeFlow().GetAwaiter().GetResult();
    });
