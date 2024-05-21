using Azure.AI.OpenAI;
using Milvus.Client;


namespace Shared.Services;

public class MilvusBase(MilvusClient client, OpenAIClient? openAIClient, string? embeddingModelName)
{
    protected readonly MilvusClient client = client;

    protected const string COLLECTION_NAME = "documents";

    protected async Task<MilvusCollection> GetOrCreateCollectionAsync()
    {
        if (await client.HasCollectionAsync(COLLECTION_NAME))
        {
            return client.GetCollection(COLLECTION_NAME);
        }

        var schema = new CollectionSchema
        {
            Fields =
            {
                FieldSchema.CreateVarchar("id", 100, isPrimaryKey: true),
                FieldSchema.CreateVarchar("content", 65000),
                FieldSchema.CreateVarchar("category", 10000),
                FieldSchema.CreateVarchar("sourcepage", 10000),
                FieldSchema.CreateVarchar("sourcefile", 10000),
                FieldSchema.CreateFloatVector("embedding", 1536),
            }
        };

        return await client.CreateCollectionAsync(COLLECTION_NAME, schema);
    }

    protected async Task<float[]> GetEmbeddingAsync(string content)
    {
        var embeddings = await openAIClient.GetEmbeddingsAsync(new Azure.AI.OpenAI.EmbeddingsOptions(embeddingModelName, [content.Replace('\r', ' ')]));
        return embeddings.Value.Data.FirstOrDefault()?.Embedding.ToArray() ?? [];
    }

}
