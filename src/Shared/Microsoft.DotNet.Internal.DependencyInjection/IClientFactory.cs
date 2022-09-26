namespace Microsoft.DotNet.Internal.DependencyInjection
{
    public interface IClientFactory<TClient>
    {
        Reference<TClient> GetClient(string name);
    }
}
