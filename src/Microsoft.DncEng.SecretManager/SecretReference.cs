namespace Microsoft.DncEng.SecretManager;

public class SecretReference
{
    public SecretReference()
    {
    }

    public SecretReference(string name)
    {
        Name = name;
    }

    public string Location { get; set; }
    public string Name { get; set; }
}
