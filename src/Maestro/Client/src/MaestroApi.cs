using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Maestro.Client
{
    partial class MaestroApiResponseClassifier
    {
        public override bool IsRetriableException(Exception exception)
        {
            return base.IsRetriableException(exception);
        }
    }

    partial class MaestroApi
    {
        partial void HandleFailedRequest(RestApiException ex)
        {
            if (ex.Response.Status == (int)HttpStatusCode.BadRequest)
            {
                JObject content;
                try
                {
                    content = JObject.Parse(ex.Response.Content);
                }
                catch (Exception)
                {
                    return;
                }

                if (content["Message"] is JValue value && value.Type == JTokenType.String)
                {
                    string message = (string)value.Value;

                    throw new ArgumentException(message, ex);
                }
            }
        }
    }
}
