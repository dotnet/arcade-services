namespace DotNet.Status.Web.Options;

public class AzureDevOpsOptions
{
    public string BaseUrl { get; set; }
    public string Organization { get; set; }
    public string Project { get; set; }
    public int MaxParallelRequests { get; set; }
    public string AccessToken { get; set; }
}
