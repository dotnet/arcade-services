// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.DarcLib.Helpers
{
    public static class LocalHelpers
    {
        public static string GetEditorPath(ILogger logger)
        {
            string editor = ExecuteCommand("git", "config --get core.editor", logger);

            // If there is nothing set in core.editor we try to default it to notepad if running in Windows, if not default it to
            // vim
            if (string.IsNullOrEmpty(editor))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    editor = ExecuteCommand("where", "notepad", logger);
                }
                else
                {
                    editor = ExecuteCommand("which", "vim", logger);
                }
            }

            // Split this by newline in case where are multiple paths;
            int newlineIndex = editor.IndexOf(System.Environment.NewLine);
            if (newlineIndex != -1)
            {
                editor = editor.Substring(0, newlineIndex);
            }

            return editor;
        }

        public static string GetRootDir(ILogger logger)
        {
            string dir = ExecuteCommand("git", "rev-parse --show-toplevel", logger);

            if (string.IsNullOrEmpty(dir))
            {
                throw new Exception("Root directory of the repo was not found. Check that git is installed and that you are in a folder which is a git repo (.git folder should be present).");
            }

            return dir;
        }

        /// <summary>
        ///     Get the current git commit sha.
        /// </summary>
        /// <param name="logger"></param>
        /// <returns></returns>
        public static string GetGitCommit(ILogger logger)
        {
            string commit = ExecuteCommand("git", "rev-parse HEAD", logger);

            if (string.IsNullOrEmpty(commit))
            {
                throw new Exception("Commit was not resolved. Check if git is installed and that a .git directory exists in the root of your repository.");
            }

            return commit;
        }

        public static string GitShow(string repoFolderPath, string commit, string fileName, ILogger logger)
        {
            string fileContents = ExecuteCommand("git", $"show {commit}:{fileName}", logger, repoFolderPath);

            if (string.IsNullOrEmpty(fileContents))
            {
                throw new Exception($"Could not show the contents of '{fileName}' at '{commit}' in '{repoFolderPath}'...");
            }

            return fileContents;
        }

        /// <summary>
        /// For each child folder in the provided "source" folder we check for the existance of a given commit. Each folder in "source"
        /// represent a different repo.
        /// </summary>
        /// <param name="sourceFolder">The main source folder.</param>
        /// <param name="commit">The commit to search for in a repo folder.</param>
        /// <param name="logger">The logger.</param>
        /// <returns></returns>
        public static string GetRepoPathFromFolder(string sourceFolder, string commit, ILogger logger)
        {
            foreach (string directory in Directory.GetDirectories(sourceFolder))
            {
                string containsCommand = ExecuteCommand("git", $"branch --contains {commit}", logger, directory);

                if (!string.IsNullOrEmpty(containsCommand))
                {
                    return directory;
                }
            }

            return null;
        }

        /// <summary>
        /// Since LibGit2Sharp doesn't support neither sparse checkout not shallow clone
        /// we implement the flow ourselves.
        /// </summary>
        /// <param name="repoUri">The repo to clone Uri</param>
        /// <param name="branch">The branch to checkout</param>
        /// <param name="workingDirectory">The working directory</param>
        /// <param name="_logger">The logger</param>
        /// <param name="remote">The name of the remote</param>
        /// <param name="user">User name</param>
        /// <param name="pat">User's personal access token</param>
        /// <param name="repoFolderName">The name of the folder where the repo is located</param>
        /// <returns>The full path of the cloned repo</returns>
        public static string SparseAndShallowCheckout(
            string repoUri,
            string branch,
            string workingDirectory,
            ILogger _logger,
            string remote,
            string user,
            string pat,
            string repoFolderName = "clonedRepo")
        {
            ExecuteCommand("git", $"init {repoFolderName}", _logger, workingDirectory);

            workingDirectory = Path.Combine(workingDirectory, repoFolderName);
            remote = remote.Replace("https://", $"https://{user}:{pat}@");

            ExecuteCommand("git", $"remote add {remote} {repoUri}", _logger, workingDirectory);
            ExecuteCommand("git", "config core.sparsecheckout true", _logger, workingDirectory);
            ExecuteCommand("cmd", "/c echo eng/ >> .git/info/sparse-checkout", _logger, workingDirectory);
            ExecuteCommand("cmd", $"/c echo /{VersionFiles.NugetConfig} >> .git/info/sparse-checkout", _logger, workingDirectory);
            ExecuteCommand("cmd", $"/c echo /{VersionFiles.GlobalJson} >> .git/info/sparse-checkout", _logger, workingDirectory);

            string result = ExecuteCommand("git", $"pull --depth=1 origin {branch}", _logger, workingDirectory);

            if (result == null)
            {
                return null;
            }

            return workingDirectory;
        }

        public static string ExecuteCommand(string fileName, string arguments, ILogger logger, string workingDirectory = null)
        {
            string output = null;

            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    FileName = fileName,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
                };

                using (Process process = new Process())
                {
                    process.StartInfo = processInfo;
                    process.StartInfo.Arguments = arguments;
                    process.Start();

                    output = process.StandardOutput.ReadToEnd().Trim();

                    process.WaitForExit();
                }
            }
            catch (Exception exc)
            {
                logger.LogWarning($"Something failed while trying to execute '{fileName} {arguments}'. Exception: {exc.Message}");
            }

            return output;
        }
    }
}
