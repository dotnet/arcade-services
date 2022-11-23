#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrRemoteConfiguration
{
    public VmrRemoteConfiguration(string? gitHubToken, string? azureDevOpsToken)
    {
        GitHubToken = gitHubToken;
        AzureDevOpsToken = azureDevOpsToken;
    }

    public string? GitHubToken { get; }

    public string? AzureDevOpsToken { get; }
}
