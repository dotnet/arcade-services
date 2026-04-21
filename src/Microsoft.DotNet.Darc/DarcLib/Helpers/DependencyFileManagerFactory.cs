// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.Helpers;

public interface IDependencyFileManagerFactory
{
    IDependencyFileManager CreateDependencyFileManager(IGitRepo gitClient);

    IDependencyFileManager CreateDependencyFileManager(IGitRepoFactory gitClientFactory);
}

public class DependencyFileManagerFactory(
    IVersionDetailsParser versionDetailsParser,
    IAssetLocationResolver resolver,
    ILoggerFactory loggerFactory)
    : IDependencyFileManagerFactory
{

    private readonly IAssetLocationResolver _assetLocationResolver = resolver;
    private readonly IVersionDetailsParser _versionDetailsParser = versionDetailsParser;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;


    public IDependencyFileManager CreateDependencyFileManager(IGitRepo gitClient)
    {
        return new DependencyFileManager(
            gitClient,
            _versionDetailsParser,
            _assetLocationResolver,
            _loggerFactory.CreateLogger<DependencyFileManager>());
    }

    public IDependencyFileManager CreateDependencyFileManager(IGitRepoFactory gitClientFactory)
    {
        return new DependencyFileManager(
            gitClientFactory,
            _versionDetailsParser,
            _assetLocationResolver,
            _loggerFactory.CreateLogger<DependencyFileManager>());
    }
}
