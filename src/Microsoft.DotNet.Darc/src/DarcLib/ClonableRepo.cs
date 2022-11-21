using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public class ClonableRepo : ClonableRepoBase, IClonableGitRepo
{
    private readonly ILogger _logger;
    private readonly string _personalAccessToken;

    public ClonableRepo(string accessToken, ILogger logger)
    {
        _logger = logger;   
        _personalAccessToken = accessToken;
    }

    /// <summary>
    ///     Clone a remote repository.
    /// </summary>
    /// <param name="repoUri">Repository uri to clone</param>
    /// <param name="commit">Commit, branch, or tag to checkout</param>
    /// <param name="targetDirectory">Directory to clone into</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
    public void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string gitDirectory = null)
    {
        Clone(repoUri, commit, targetDirectory, checkoutSubmodules, _logger, _personalAccessToken, gitDirectory);
    }
}
