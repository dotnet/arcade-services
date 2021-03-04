using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Octokit;

namespace DotNet.Status.Web.Models
{
    internal static class GitHubModifications
    {
        public static async Task CreateLabelsAsync(
            IGitHubClient client,
            string org,
            string repo,
            ILogger logger,
            IEnumerable<NewLabel> desiredLabels)
        {
            logger.LogInformation("Ensuring tags exist");

            IReadOnlyList<Label> labels = await client.Issue.Labels.GetAllForRepository(org, repo);

            async Task MakeLabel(NewLabel label)
            {
                if (labels.All(l => l.Name != label.Name))
                {
                    logger.LogInformation("Missing tag {tag}, creating...", label.Name);
                    await TryCreateAsync(() =>
                            client.Issue.Labels.Create(org, repo, label),
                        logger);
                }
            }

            await Task.WhenAll(desiredLabels.Select(MakeLabel));

            logger.LogInformation("Tags ensured");
        }

        public static async Task TryCreateAsync(Func<Task> createFunc, ILogger logger)
        {
            try
            {
                await createFunc();
            }
            catch (ApiValidationException e) when (e.ApiError.Errors.Any(r => r.Code == "already_exists"))
            {
                logger.LogWarning("github resource already exists: {exception}", e);
            }
        }

        public static async Task TryRemoveAsync(Func<Task> removeFunc, ILogger logger)
        {
            try
            {
                await removeFunc();
            }
            catch (NotFoundException e)
            {
                logger.LogWarning("github resource not found: {exception}", e);
            }
        }
    }
}
