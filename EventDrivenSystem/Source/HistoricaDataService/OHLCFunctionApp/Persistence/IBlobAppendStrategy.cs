using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
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
    }
}
