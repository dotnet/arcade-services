using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IClonableRepoFactory
{
    public IClonableGitRepo GetRemote(string repoUrl, ILogger logger);
}

public class ClonableRepoFactory : IClonableRepoFactory
{
    private readonly VmrRemoteConfiguration _vmrRemoteConfig;

    public ClonableRepoFactory(VmrRemoteConfiguration vmrRemoteConfig)
    {
        _vmrRemoteConfig = vmrRemoteConfig;
    }

    public IClonableGitRepo GetRemote(string repoUrl, ILogger logger)
    {
        Uri repoUri = new(repoUrl);

        IClonableGitRepo gitClient;

        if (repoUri.IsFile)
        {
            gitClient = new ClonableRepo(null, logger);
        } 
        else if(repoUri.Host == "github.com")
        {
            gitClient = new ClonableRepo(_vmrRemoteConfig.GitHubToken, logger);
        }
        else if (repoUri.Host == "dev.azure.com" || repoUri.Host == "visualstudio.com")
        {
            gitClient = new ClonableRepo(_vmrRemoteConfig.AzureDevOpsToken, logger);
        }
        else
        {
            throw new NotImplementedException($"Unknown repo url type {repoUrl}");
        }
        

        return gitClient;
    }
}


