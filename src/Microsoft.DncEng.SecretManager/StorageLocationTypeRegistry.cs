using System;
using System.Collections.Generic;

namespace Microsoft.DncEng.SecretManager;

public class StorageLocationTypeRegistry : NamedObjectRegistry<StorageLocationType>
{
    public StorageLocationTypeRegistry() : base()
    {
    }

    public StorageLocationTypeRegistry(IEnumerable<StorageLocationType> objects) : base(objects)
    {
    }
}
