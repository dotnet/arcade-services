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
    IServiceProvider serviceProvider,
    ILogger<StatusController> logger) : ControllerBase
{
    [HttpGet]
    public async Task GetStatusAsync()
    {
        Response.ContentType = "text/html; charset=utf-8";
        var stream = Response.Body;

        await WriteAndFlushAsync(stream, "<html><body style='font-family:monospace;white-space:pre-wrap;word-break:break-word;padding:20px'>\n");
        await WriteAndFlushAsync(stream, $"=== DummyApp Resource Status ({DateTimeOffset.UtcNow:u}) ===<br/><br/>");

        await CheckSqlAsync(stream);
        await WriteAndFlushAsync(stream, "<br/>");
        await CheckRedisAsync(stream);
        await WriteAndFlushAsync(stream, "<br/>");
        await CheckBlobStorageAsync(stream);

        await WriteAndFlushAsync(stream, "<br/>=== Done ===<br/>");
        await WriteAndFlushAsync(stream, "</body></html>");
    }

    private static async Task WriteAndFlushAsync(Stream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await stream.FlushAsync();
    }

    private static Task WriteFailureAsync(Stream stream, long elapsedMs, string message)
        => WriteAndFlushAsync(stream, $"  <span style='color:red'>FAILED ({elapsedMs} ms): {message}</span><br/>");

    private async Task CheckSqlAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[SQL Server]<br/>");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking SQL Server connectivity");

            var sqlConnection = serviceProvider.GetRequiredService<SqlConnection>();
            await sqlConnection.OpenAsync();
            await using var command = sqlConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";
            var tableCount = await command.ExecuteScalarAsync();
            await sqlConnection.CloseAsync();
            sw.Stop();

            logger.LogInformation("SQL Server check completed, found {TableCount} tables", tableCount);

            await WriteAndFlushAsync(stream,
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)<br/>" +
                $"  Tables in database: {tableCount}<br/>");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "SQL Server check failed");
            await WriteFailureAsync(stream, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task CheckRedisAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[Redis]<br/>");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking Redis connectivity");

            var redisConnection = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            const string testKey = "dummy-app:health-check";
            const string testValue = "ok";

            IDatabase db = redisConnection.GetDatabase();
            await db.StringSetAsync(testKey, testValue, TimeSpan.FromSeconds(30));
            string? readBack = await db.StringGetAsync(testKey);
            await db.KeyDeleteAsync(testKey);
            sw.Stop();

            logger.LogInformation("Redis check completed, read back value: {Value}", readBack);

            await WriteAndFlushAsync(stream,
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)<br/>" +
                $"  Wrote key '{testKey}' = '{testValue}'<br/>" +
                $"  Read back: '{readBack}'<br/>" +
                $"  Key deleted<br/>");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Redis check failed");
            await WriteFailureAsync(stream, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task CheckBlobStorageAsync(Stream stream)
    {
        await WriteAndFlushAsync(stream, "[Blob Storage]<br/>");
        var sw = Stopwatch.StartNew();
        try
        {
            logger.LogInformation("Checking Blob Storage connectivity");

            var blobServiceClient = serviceProvider.GetRequiredService<BlobServiceClient>();
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
                $"  Connected OK ({sw.ElapsedMilliseconds} ms)<br/>" +
                $"  Created container '{containerName}'<br/>" +
                $"  Uploaded blob '{blobName}' with content '{blobContent}'<br/>" +
                $"  Downloaded and read back: '{readBack}'<br/>" +
                $"  Blob deleted<br/>");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Blob Storage check failed");
            await WriteFailureAsync(stream, sw.ElapsedMilliseconds, ex.Message);
        }
    }
}
