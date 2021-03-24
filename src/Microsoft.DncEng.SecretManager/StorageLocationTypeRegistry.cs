using System;

namespace Microsoft.DncEng.SecretManager
{
    public class StorageLocationTypeRegistry : NamedObjectRegistry<StorageLocationType>
    {
        public StorageLocationTypeRegistry(IServiceProvider provider) : base(provider)
        {
        }
    }
}