using System.Net;
using System.Text.RegularExpressions;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Code.Services;


public class UrlRedirectManager
{
    private static Regex _repoUrlGitHubRegex = new Regex(@"^\/(?<channelId>[^\/]+)\/(?<repoUrl>[^\/]+)\/(?<buildId>[^\/]+)\/graph$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string? ApplyLocatinoRedirects(string location)
    {
        Uri uri = new Uri(location);

        var path = uri.PathAndQuery;

        Match m = _repoUrlGitHubRegex.Match(path);
        if (m.Success)
        {
            string repoUrl = WebUtility.UrlDecode(m.Groups["repoUrl"].Value);
            string? repoSlug = RepoUrlConverter.RepoUrlToSlug(repoUrl);
            if (repoSlug != null)
            {
                string channelId = m.Groups["channelId"].Value;
                string buildId = m.Groups["buildId"].Value;
                return $"/channel/{channelId}/{repoSlug}/build/{buildId}";
            }
        }
        return null;
    }
}
