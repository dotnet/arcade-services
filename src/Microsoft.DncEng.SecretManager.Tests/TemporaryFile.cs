using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DncEng.SecretManager.Tests
{
    public class TemporaryFile : IDisposable
    {
        public string FilePath { get; }
        private readonly Stream _file;

        public TemporaryFile()
        {
            FilePath = Path.GetTempFileName();
        }

        public async Task WriteAllTextAsync(string text)
        {
            using var file = new FileStream(FilePath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(file);
            await writer.WriteAsync(text);
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }
}
