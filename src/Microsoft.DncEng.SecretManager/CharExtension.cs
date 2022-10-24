namespace Microsoft.DncEng.SecretManager;

internal static class CharExtension
{
    public static bool IsHexChar(this char c)
    {
        return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
