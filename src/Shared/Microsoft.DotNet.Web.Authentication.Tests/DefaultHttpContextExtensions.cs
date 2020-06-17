using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    internal static class DefaultHttpContextExtensions
    {
        internal static void SetUrl(this HttpRequest req, string url)
        {
            UriHelper.FromAbsolute(url,
                out string scheme,
                out HostString host,
                out PathString path,
                out QueryString queryString,
                out _
            );
            req.Scheme = scheme;
            req.Host = host;
            req.Path = path;
            req.QueryString = queryString;
        }
    }
}
