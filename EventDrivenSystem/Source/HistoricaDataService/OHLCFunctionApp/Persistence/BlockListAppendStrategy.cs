using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OHLCFunctionApp.Persistence
{
    // Meant for real Azure — stages the new row as its own block and commits an updated block
    // list, so it never re-transfers the file's existing content the way
    // SimpleReuploadAppendStrategy does. NOT the default; switched on via BLOB_APPEND_STRATEGY
    // once this actually points at production Azure (see Program.cs / README).
    //
    // ⚠️ Not independently verified against real Azure from this environment — only Azurite is
    // available here, and Azurite's block-list emulation may not enforce every real-Azure
    // constraint with the same strictness. Test carefully against a real storage account before
    // relying on this in production.
    //
    // The real constraint this has to work around: Azure requires every block ID in a single
    // CommitBlockList call to be the same length. A blob's existing committed block(s) — from
    // whatever process originally created it via a plain UploadAsync — used whatever length the
    // SDK/tooling picked at the time, which this strategy doesn't control. So the new block's ID
    // is generated to match the length of an existing committed block's ID, read back from
    // GetBlockListAsync, rather than assuming any fixed length.
    public class BlockListAppendStrategy : IBlobAppendStrategy
    {
        private const int MaxRetries = 5;
        private const int DefaultBlockIdLength = 44; // typical base64 SDK-generated block ID length; only used when creating a brand-new blob with no prior blocks to match

        public async Task AppendAsync(BlobContainerClient container, string blobPath, string headerLine, string dataLine, ILogger logger)
        {
            var blobClient = container.GetBlockBlobClient(blobPath);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (!await blobClient.ExistsAsync())
                {
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
                        logger.LogInformation($"{blobPath}: lost the create race, retrying as a staged append (attempt {attempt}/{MaxRetries})");
                        await Task.Delay(RetryDelayMs(attempt));
                        continue;
                    }
                }

                try
                {
                    var blockList = await blobClient.GetBlockListAsync(BlockListTypes.Committed);
                    var existingBlockIds = blockList.Value.CommittedBlocks.Select(b => b.Name).ToList();
                    var props = await blobClient.GetPropertiesAsync();

                    int blockIdLength = existingBlockIds.Count > 0
                        ? existingBlockIds[0].Length
                        : DefaultBlockIdLength;
                    string newBlockId = GenerateBlockId(blockIdLength);

                    using var rowStream = new MemoryStream(Encoding.UTF8.GetBytes(dataLine + "\n"));
                    await blobClient.StageBlockAsync(newBlockId, rowStream);

                    existingBlockIds.Add(newBlockId);
                    await blobClient.CommitBlockListAsync(existingBlockIds, new CommitBlockListOptions
                    {
                        Conditions = new BlobRequestConditions { IfMatch = props.Value.ETag }
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

        // Block IDs are opaque strings up to 64 bytes once base64-decoded; a GUID's 16 bytes,
        // base64-encoded, is comfortably under that and unique per call. Padded/truncated to match
        // whatever length the blob's existing blocks already use, since Azure requires every block
        // ID in one CommitBlockList call to share a single length.
        private static string GenerateBlockId(int targetLength)
        {
            string id = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            if (id.Length >= targetLength) return id.Substring(0, targetLength);
            return id.PadRight(targetLength, 'A');
        }

        private static int RetryDelayMs(int attempt) => 50 * attempt;
    }
}
