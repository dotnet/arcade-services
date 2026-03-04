using System.Diagnostics;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace BuildInsights.DummyApp.Controllers;

[ApiController]
[Route("/status")]
public class StatusController(
    SqlConnection sqlConnection,
    IConnectionMultiplexer redisConnection,
    BlobServiceClient blobServiceClient,
    ILogger<StatusController> logger) : ControllerBase
{
    [HttpGet]
    public async Task GetStatusAsync()
    {
        Response.ContentType = "text/html; charset=utf-8";
        var stream = Response.Body;

        await WriteAndFlushAsync(stream, "<html><body style='font-family:monospace;white-space:pre;padding:20px'>\n");
        await WriteAndFlushAsync(stream, $"=== DummyApp Resource Status ({DateTimeOffset.UtcNow:u}) ===\n\n");

        await CheckSqlAsync(stream);
        await WriteAndFlushAsync(stream, "\n");
        await CheckRedisAsync(stream);
        await WriteAndFlushAsync(stream, "\n");
        await CheckBlobStorageAsync(stream);

        await WriteAndFlushAsync(stream, "\n=== Done ===\n");
        await WriteAndFlushAsync(stream, "</body></html>");
    }

    private static async Task WriteAndFlushAsync(Stream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await stream.FlushAsync();
    }

    private async Task CheckSqlAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[SQL Server]\n");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking SQL Server connectivity");

            await sqlConnection.OpenAsync();
            await using var command = sqlConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";
            var tableCount = await command.ExecuteScalarAsync();
            await sqlConnection.CloseAsync();
            sw.Stop();

            logger.LogInformation("SQL Server check completed, found {TableCount} tables", tableCount);

            await WriteAndFlushAsync(stream,
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)\n" +
                $"  Tables in database: {tableCount}\n");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "SQL Server check failed");
            await WriteAndFlushAsync(stream, $"  FAILED ({sw.ElapsedMilliseconds} ms): {ex.Message}\n");
        }
    }

    private async Task CheckRedisAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[Redis]\n");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking Redis connectivity");

            const string testKey = "dummy-app:health-check";
            const string testValue = "ok";

            IDatabase db = redisConnection.GetDatabase();
            await db.StringSetAsync(testKey, testValue, TimeSpan.FromSeconds(30));
            string? readBack = await db.StringGetAsync(testKey);
            await db.KeyDeleteAsync(testKey);
            sw.Stop();

            logger.LogInformation("Redis check completed, read back value: {Value}", readBack);

            await WriteAndFlushAsync(stream,
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)\n" +
                $"  Wrote key '{testKey}' = '{testValue}'\n" +
                $"  Read back: '{readBack}'\n" +
                $"  Key deleted\n");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Redis check failed");
            await WriteAndFlushAsync(stream, $"  FAILED ({sw.ElapsedMilliseconds} ms): {ex.Message}\n");
        }
    }

    private async Task CheckBlobStorageAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[Blob Storage]\n");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking Blob Storage connectivity");

            const string containerName = "health-check";
            const string blobName = "dummy-app-probe.txt";
            const string blobContent = "health-check-ok";

            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(blobName);
            using (var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(blobContent)))
            {
                await blobClient.UploadAsync(stream2, overwrite: true);
            }

            var downloadResult = await blobClient.DownloadContentAsync();
            string readBack = downloadResult.Value.Content.ToString();

            await blobClient.DeleteAsync();
            sw.Stop();

            logger.LogInformation("Blob Storage check completed, read back value: {Value}", readBack);

            await WriteAndFlushAsync(stream,
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)\n" +
                $"  Created container '{containerName}'\n" +
                $"  Uploaded blob '{blobName}' with content '{blobContent}'\n" +
                $"  Downloaded and read back: '{readBack}'\n" +
                $"  Blob deleted\n");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Blob Storage check failed");
            await WriteAndFlushAsync(stream, $"  FAILED ({sw.ElapsedMilliseconds} ms): {ex.Message}\n");
        }
    }
}
