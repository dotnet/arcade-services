using System;
using System.IO;

namespace SubscriptionActorService
{
    public interface ILocalGit
    {
        string GetPathToLocalGit();
    }

    public class LocalGit : ILocalGit
    {
        public string GetPathToLocalGit()
        {
            var gitExePath = Path.Join(AppContext.BaseDirectory, "git-portable", "bin", "git.exe");
            if (!File.Exists(gitExePath))
            {
                throw new InvalidOperationException(
                    $"Portable git not found at path {gitExePath}, the build needs to be configured to publish it inside the service package.");
            }

            return gitExePath;
        }
    }
}
