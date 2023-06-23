using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.Tests;

public class TemporaryFile : IDisposable
{
    public string FilePath { get; }

    public TemporaryFile()
    {
        FilePath = Path.GetTempFileName();
    }

    public async Task WriteAllTextAsync(string text)
    {
        await File.WriteAllTextAsync(FilePath, text);
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
