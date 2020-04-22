using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Kusto;

namespace Maestro.Web
{
    public static class Helpers
    {
        public static string GetApplicationVersion()
        {
            Assembly assembly = typeof(Helpers).Assembly;
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoVersion != null)
            {
                return infoVersion.InformationalVersion;
            }

            var version = assembly.GetCustomAttribute<AssemblyVersionAttribute>();
            if (version != null)
            {
                return version.Version;
            }

            return "42.42.42.42";
        }

        public static async Task<IActionResult> ProxyRequestAsync(this HttpContext context, HttpClient client, string targetUrl, Action<HttpRequestMessage> configureRequest)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, targetUrl))
            {
                foreach (var (key, values) in context.Request.Headers)
                {
                    switch (key.ToLower())
                    {
                        // We shouldn't copy any of these request headers
                        case "host":
                        case "authorization":
                        case "cookie":
                        case "content-length":
                        case "content-type":
                            continue;
                        default:
                            req.Headers.Add(key, values.ToArray());
                            break;
                    }
                }

                configureRequest(req);

                HttpResponseMessage res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                context.Response.RegisterForDispose(res);

                foreach (var (key, values) in res.Headers)
                {
                    switch (key.ToLower())
                    {
                        // Remove headers that the response doesn't need
                        case "set-cookie":
                        case "x-powered-by":
                        case "x-aspnet-version":
                        case "server":
                        case "transfer-encoding":
                        case "access-control-expose-headers":
                        case "access-control-allow-origin":
                            continue;
                        default:
                            if (!context.Response.Headers.ContainsKey(key))
                            {
                                context.Response.Headers.Add(key, values.ToArray());
                            }

                            break;
                    }
                }


                context.Response.StatusCode = (int) res.StatusCode;
                if (res.Content != null)
                {
                    foreach (var (key, values) in res.Content.Headers)
                    {
                        if (!context.Response.Headers.ContainsKey(key))
                        {
                            context.Response.Headers.Add(key, values.ToArray());
                        }
                    }

                    using (var data = await res.Content.ReadAsStreamAsync())
                    {
                        await data.CopyToAsync(context.Response.Body);
                    }
                }

                return new EmptyResult();
            }
        }
    }
}
