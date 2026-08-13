using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OHLCFunctionApp.Persistence
{
    // Two implementations, chosen by config (BLOB_APPEND_STRATEGY env var — see Program.cs):
    // SimpleReuploadAppendStrategy (local/Azurite default) and BlockListAppendStrategy (meant for
    // real Azure, where re-uploading the whole growing file on every 1-min candle gets genuinely
    // costly). Both are ETag-conditional with retry, so a race between two candles landing close
    // together on the same blob can't silently lose a row.
    public interface IBlobAppendStrategy
    {
        // headerLine is only used the first time a blob is created for a given month/contract;
        // every call after that just appends dataLine.
        Task AppendAsync(BlobContainerClient container, string blobPath, string headerLine, string dataLine, ILogger logger);

        // Creates blobPath with just headerLine if it doesn't exist yet; a no-op (not an error) if
        // it already exists — including if it already has data rows in it, so this is always safe
        // to call even if a warm-up runs after some candles have already landed (e.g. a restart
        // mid-day). Used to pre-create today's daily files at NSE's Init event.
        Task EnsureHeaderAsync(BlobContainerClient container, string blobPath, string headerLine, ILogger logger);

        // Used by the daily->monthly Close-triggered merge. Removes any existing data rows in
        // blobPath whose date field starts with datePrefix ("dd-MM-yyyy"), then appends newDataLines
        // at the end — makes the merge safe to re-run for the same day (e.g. a redelivered Close
        // event, or a manual re-trigger) without doubling that day's rows: the stale copy is dropped
        // and the fresh one lands in the same place, at the end, instead of accumulating duplicates.
        // Always a full read+filter+rewrite in one round trip regardless of strategy — removing
        // existing content isn't something block-staging can do incrementally, so BlockListAppendStrategy's
        // usual never-re-transfer-existing-content property doesn't apply to this one operation.
        Task MergeReplacingDateAsync(BlobContainerClient container, string blobPath, string headerLine, string datePrefix, IEnumerable<string> newDataLines, ILogger logger);
    }
}
