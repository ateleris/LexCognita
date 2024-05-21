

namespace EmbedFunctions.Services;

public sealed class EmbeddingAggregateService(
    MilvusEmbedService embedService,
    BlobServiceClient blobServiceClient,
    BlobContainerClient corpusClient,
    ILogger<EmbeddingAggregateService> logger)
{
    internal async Task EmbedBlobAsync(Stream blobStream, string blobName)
    {
        try
        {
            if (Path.GetExtension(blobName) is ".png" or ".jpg" or ".jpeg" or ".gif")
            {
                throw new NotImplementedException();
            }
            else if (Path.GetExtension(blobName) is ".pdf")
            {
                logger.LogInformation("Embedding pdf: {Name}", blobName);
                var result = await embedService.EmbedPDFBlobAsync(blobStream, blobName);

                var status = result switch
                {
                    true => DocumentProcessingStatus.Succeeded,
                    _ => DocumentProcessingStatus.Failed
                };

                await corpusClient.SetMetadataAsync(new Dictionary<string, string>
                {
                    [nameof(DocumentProcessingStatus)] = status.ToString(),
                });
            }
            else
            {
                throw new NotSupportedException("Unsupported file type.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to embed: {Name}, error: {Message}", blobName, ex.Message);
            throw;
        }
    }
}
