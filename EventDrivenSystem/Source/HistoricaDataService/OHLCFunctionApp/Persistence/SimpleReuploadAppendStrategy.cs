using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public async Task EnsureHeaderAsync(BlobContainerClient container, string blobPath, string headerLine, ILogger logger)
        {
            var blobClient = container.GetBlobClient(blobPath);
            if (await blobClient.ExistsAsync())
            {
                logger.LogInformation($"{blobPath}: already exists, header ensure is a no-op.");
                return;
            }

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(headerLine + "\n"));
            try
            {
                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                });
                logger.LogInformation($"{blobPath}: created with header row.");
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Lost the create race — something else (a live candle, or a concurrent warm-up
                // retry) already created this blob. The desired end state ("blob exists") is
                // already true, so there's nothing left to do.
                logger.LogInformation($"{blobPath}: lost the create race, blob already exists — no-op.");
            }
        }

        public async Task MergeReplacingDateAsync(BlobContainerClient container, string blobPath, string headerLine, string datePrefix, IEnumerable<string> newDataLines, ILogger logger)
        {
            var newLines = newDataLines as IReadOnlyList<string> ?? newDataLines.ToList();
            var blobClient = container.GetBlobClient(blobPath);
            string matchPrefix = datePrefix + " ";

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (!await blobClient.ExistsAsync())
                {
                    if (newLines.Count == 0)
                    {
                        logger.LogInformation($"{blobPath}: doesn't exist yet and there's nothing to merge in, skipping.");
                        return;
                    }

                    string initialContent = headerLine + "\n" + string.Join("\n", newLines) + "\n";
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
                        logger.LogInformation($"{blobPath}: lost the create race, retrying as a merge (attempt {attempt}/{MaxRetries})");
                        await Task.Delay(RetryDelayMs(attempt));
                        continue;
                    }
                }

                try
                {
                    var download = await blobClient.DownloadContentAsync();
                    string existing = download.Value.Content.ToString();
                    var existingLines = existing.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                    string header = existingLines.Count > 0 ? existingLines[0] : headerLine;

                    // Drop any existing row for the date being merged in — a stale copy from an
                    // earlier merge of the same day — then append the fresh rows at the end, after
                    // whatever survived the filter. Never inserted mid-file: rows for a given day are
                    // always contiguous at the tail already, since the monthly file only ever grows
                    // forward in time.
                    var keptRows = existingLines.Skip(1).Where(l => !l.StartsWith(matchPrefix, StringComparison.Ordinal)).ToList();

                    var rebuilt = new List<string> { header };
                    rebuilt.AddRange(keptRows);
                    rebuilt.AddRange(newLines);

                    string updated = string.Join("\n", rebuilt) + "\n";
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

            throw new InvalidOperationException($"Failed to merge {newLines.Count} row(s) into {blobPath} after {MaxRetries} attempts due to repeated concurrent write conflicts.");
        }

        private static int RetryDelayMs(int attempt) => 50 * attempt; // mild backoff, not exponential — conflicts here are expected to be rare and short-lived
    }
}
