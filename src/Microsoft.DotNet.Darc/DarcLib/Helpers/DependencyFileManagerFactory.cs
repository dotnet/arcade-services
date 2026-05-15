// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.Helpers;

public interface IDependencyFileManagerFactory
{
    IDependencyFileManager CreateDependencyFileManager(IGitRepo gitClient);

    IDependencyFileManager CreateDependencyFileManager();
}

public class DependencyFileManagerFactory(
    IVersionDetailsParser versionDetailsParser,
    IAssetLocationResolver resolver,
    IGitRepoFactory gitRepoFactory,
    ILoggerFactory loggerFactory)
    : IDependencyFileManagerFactory
{

    private readonly IAssetLocationResolver _assetLocationResolver = resolver;
    private readonly IVersionDetailsParser _versionDetailsParser = versionDetailsParser;
    private readonly IGitRepoFactory _gitRepoFactory = gitRepoFactory;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;


    public IDependencyFileManager CreateDependencyFileManager(IGitRepo gitClient)
    {
        return new DependencyFileManager(
            gitClient,
            _versionDetailsParser,
            _assetLocationResolver,
            _loggerFactory.CreateLogger<DependencyFileManager>());
    }

    public IDependencyFileManager CreateDependencyFileManager()
    {
        return new DependencyFileManager(
            _gitRepoFactory,
            _versionDetailsParser,
            _assetLocationResolver,
            _loggerFactory.CreateLogger<DependencyFileManager>());
    }
}
