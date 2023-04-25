namespace Microsoft.DotNet.Internal.DependencyInjection;

public class SingleClientFactory<T> : IClientFactory<T>
{
    private readonly RefCountedObject<T> _client;

    public SingleClientFactory(T client)
    {
        _client = new RefCountedObject<T>(client);
    }
        
    public Reference<T> GetClient(string name)
    {
        return new Reference<T>(_client);
    }
}
