namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

//https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.build.webapi.repositorytypes?view=azure-devops-dotnet
public enum BuildRepositoryType
{
    Unknown,
    Git,
    GitHub,
    TfsGit, //Team Foundation Server Git
    TfsVersionControl
}
