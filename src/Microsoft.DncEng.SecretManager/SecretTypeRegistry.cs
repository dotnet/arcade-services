using System;
using System.Collections.Generic;

namespace Microsoft.DncEng.SecretManager;

public class SecretTypeRegistry : NamedObjectRegistry<SecretType>
{
    protected SecretTypeRegistry() : base()
    {
    }

    public SecretTypeRegistry(IEnumerable<SecretType> objects) : base(objects)
    {
    }
}
