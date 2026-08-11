using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OHLCFunctionApp.Persistence
{
    // Default strategy — correct and simple, at the cost of re-transferring the whole existing
    // file's content on every single append. Files are small at current volume (~650KB/month) and
    // this points at local Azurite, so the redundant transfer is free in practice. This is the
    // strategy actually exercised end-to-end; BlockListAppendStrategy is the one meant for real
    // Azure and hasn't had the same live validation.
    public class SimpleReuploadAppendStrategy : IBlobAppendStrategy
    {
        private const int MaxRetries = 5;

        public async Task AppendAsync(BlobContainerClient container, string blobPath, string headerLine, string dataLine, ILogger logger)
        {
            var blobClient = container.GetBlobClient(blobPath);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (!await blobClient.ExistsAsync())
                {
                    // Create-only write (IfNoneMatch: "*") — if another instance's candle for the
                    // same brand-new blob wins the race, this throws 412 and we fall into the retry
                    // loop, which will now see the blob exists and append normally instead of
                    // clobbering whatever the winner just wrote.
                    string initialContent = headerLine + "\n" + dataLine + "\n";
                    using var initialStream = new MemoryStream(Encoding.UTF8.GetBytes(initialContent));

                    try
                    {
                        await blobClient.UploadAsync(initialStream, new BlobUploadOptions
                        {
                            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                        });
                        return;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 412)
                    {
                        logger.LogInformation($"{blobPath}: lost the create race, retrying as an append (attempt {attempt}/{MaxRetries})");
                        await Task.Delay(RetryDelayMs(attempt));
                        continue;
                    }
                }

                try
                {
                    var download = await blobClient.DownloadContentAsync();
                    string existing = download.Value.Content.ToString();
                    string updated = existing.TrimEnd('\r', '\n') + "\n" + dataLine + "\n";
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(updated));

                    await blobClient.UploadAsync(stream, new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfMatch = download.Value.Details.ETag }
                    });
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    logger.LogWarning($"{blobPath}: concurrent write detected, retrying (attempt {attempt}/{MaxRetries})");
                    await Task.Delay(RetryDelayMs(attempt));
                }
            }

            throw new InvalidOperationException($"Failed to append to {blobPath} after {MaxRetries} attempts due to repeated concurrent write conflicts.");
        }

        private static int RetryDelayMs(int attempt) => 50 * attempt; // mild backoff, not exponential — conflicts here are expected to be rare and short-lived
    }
}
