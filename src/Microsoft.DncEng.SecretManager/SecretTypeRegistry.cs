using System;

namespace Microsoft.DncEng.SecretManager
{
    public class SecretTypeRegistry : NamedObjectRegistry<SecretType>
    {
        public SecretTypeRegistry() : base(null)
        {
        }

        public SecretTypeRegistry(IServiceProvider provider) : base(provider)
        {
        }
    }
}
