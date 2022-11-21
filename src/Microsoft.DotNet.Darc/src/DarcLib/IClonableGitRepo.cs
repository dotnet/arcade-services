namespace Microsoft.DotNet.DarcLib;

public interface IClonableGitRepo
{
    /// <summary>
    ///     Clone a remote repository.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Directory to clone to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for .git directory, or null for default</param>
    void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string gitDirectory = null);
}
