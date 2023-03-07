namespace Microsoft.DncEng.SecretManager;

public class SecretPropertiesWrapper
{
    public SecretProperties Properties { get; }
    public bool NextRotationFound { get; }

    public SecretPropertiesWrapper(SecretProperties properties, bool nextRotationFound) 
    {
        Properties = properties;
        NextRotationFound = nextRotationFound;
    }
}
