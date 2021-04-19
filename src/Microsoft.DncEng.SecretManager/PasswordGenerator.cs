using System.Security.Cryptography;
using System.Text;

public static class PasswordGenerator
{
    public static string GenerateRandomPassword(int length)
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetNonZeroBytes(bytes);
        var result = new StringBuilder(length);
        foreach (byte b in bytes)
        {
            int value = b % 62;
            char c;
            if (value < 26)
            {
                c = (char)('A' + value);
            }
            else if (value < 52)
            {
                c = (char)('a' + value - 26);
            }
            else
            {
                c = (char)('0' + value - 52);
            }

            result.Append(c);
        }

        return result.ToString();
    }
}