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

        // Bulk variant of AppendAsync — appends every line in dataLines in one read+write round trip
        // (Simple) / one stage+commit (BlockList), not one round trip per line. headerLine is only
        // used if blobPath doesn't exist yet. Used by the daily->monthly Close-triggered merge.
        Task AppendManyAsync(BlobContainerClient container, string blobPath, string headerLine, IEnumerable<string> dataLines, ILogger logger);
    }
}
