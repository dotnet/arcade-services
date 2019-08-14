// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.DarcLib.Helpers
{
    public static class LocalHelpers
    {
        public static string GetEditorPath(string gitLocation, ILogger logger)
        {
            string editor = ExecuteCommand(gitLocation, "config --get core.editor", logger);

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

        public static string GetRootDir(string gitLocation, ILogger logger)
        {
            string dir = ExecuteCommand(gitLocation, "rev-parse --show-toplevel", logger);

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
        public static string GetGitCommit(string gitLocation, ILogger logger)
        {
            string commit = ExecuteCommand(gitLocation, "rev-parse HEAD", logger);

            if (string.IsNullOrEmpty(commit))
            {
                throw new Exception("Commit was not resolved. Check if git is installed and that a .git directory exists in the root of your repository.");
            }

            return commit;
        }

        public static string GitShow(string gitLocation, string repoFolderPath, string commit, string fileName, ILogger logger)
        {
            string fileContents = ExecuteCommand(gitLocation, $"show {commit}:{fileName}", logger, repoFolderPath);

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
        public static string GetRepoPathFromFolder(string gitLocation, string sourceFolder, string commit, ILogger logger)
        {
            foreach (string directory in Directory.GetDirectories(sourceFolder))
            {
                string containsCommand = ExecuteCommand(gitLocation, $"branch --contains {commit}", logger, directory);

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
        /// <param name="logger">The logger</param>
        /// <param name="remote">The name of the remote</param>
        /// <param name="user">User name</param>
        /// <param name="email">User's email</param>
        /// <param name="pat">User's personal access token</param>
        /// <param name="repoFolderName">The name of the folder where the repo is located</param>
        /// <returns>The full path of the cloned repo</returns>
        public static string SparseAndShallowCheckout(
            string gitLocation,
            string repoUri,
            string branch,
            string workingDirectory,
            ILogger logger,
            string remote,
            string user,
            string email,
            string pat,
            string repoFolderName = "clonedRepo")
        {
            Directory.CreateDirectory(workingDirectory);

            ExecuteGitShallowSparseCommand(gitLocation, $"init {repoFolderName}", logger, workingDirectory);

            workingDirectory = Path.Combine(workingDirectory, repoFolderName);
            repoUri = repoUri.Replace("https://", $"https://{user}:{pat}@");

            ExecuteGitShallowSparseCommand(gitLocation, $"remote add {remote} {repoUri}", logger, workingDirectory);
            ExecuteGitShallowSparseCommand(gitLocation, "config core.sparsecheckout true", logger, workingDirectory);
            ExecuteGitShallowSparseCommand(gitLocation, "config core.longpaths true", logger, workingDirectory);
            ExecuteGitShallowSparseCommand(gitLocation, $"config user.name {user}", logger, workingDirectory);
            ExecuteGitShallowSparseCommand(gitLocation, $"config user.email {email}", logger, workingDirectory);

            File.WriteAllLines(Path.Combine(workingDirectory, ".git/info/sparse-checkout"), new[] { "eng/", $"/{VersionFiles.NugetConfig}", $"/{VersionFiles.GlobalJson}" });

            ExecuteGitShallowSparseCommand(gitLocation, $"pull --depth=1 {remote} {branch}", logger, workingDirectory);
            ExecuteGitShallowSparseCommand(gitLocation, $"checkout {branch}", logger, workingDirectory);

            return workingDirectory;
        }

        public static string ExecuteCommand(string command, string arguments, ILogger logger, string workingDirectory = null)
        {
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("Executable command must be non-empty");
            }

            string output = null;

            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    FileName = command,
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
                logger.LogWarning($"Something failed while trying to execute '{command} {arguments}'. Exception: {exc.Message}");
            }

            return output;
        }

        private static void ExecuteGitShallowSparseCommand(string gitLocation, string arguments, ILogger logger, string workingDirectory)
        {
            using (logger.BeginScope("Executing command git {arguments} in {workingDirectory}...", arguments, workingDirectory))
            {
                string result = ExecuteCommand(gitLocation, arguments, logger, workingDirectory);

                if (result == null)
                {
                    throw new DarcException($"Something failed when executing command git {arguments} in {workingDirectory}");
                }
            }
        }
    }
}
