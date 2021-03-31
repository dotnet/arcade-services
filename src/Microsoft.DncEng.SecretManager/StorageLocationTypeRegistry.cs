using System;

namespace Microsoft.DncEng.SecretManager
{
    public class StorageLocationTypeRegistry : NamedObjectRegistry<StorageLocationType>
    {
        public StorageLocationTypeRegistry() : base(null)
        {
        }
        public StorageLocationTypeRegistry(IServiceProvider provider) : base(provider)
        {
        }
    }
}
