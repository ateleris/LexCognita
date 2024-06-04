

using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EmbedFunctions.Services;
using Milvus.Client;
using PrepareDocs;

internal static partial class Program
{
    private static BlobContainerClient? s_corpusContainerClient;
    private static BlobContainerClient? s_containerClient;
    private static DocumentAnalysisClient? s_documentClient;
    private static OpenAIClient? s_openAIClient;

    private static readonly SemaphoreSlim s_corpusContainerLock = new(1);
    private static readonly SemaphoreSlim s_containerLock = new(1);
    private static readonly SemaphoreSlim s_documentLock = new(1);
    private static readonly SemaphoreSlim s_searchIndexLock = new(1);
    private static readonly SemaphoreSlim s_searchLock = new(1);
    private static readonly SemaphoreSlim s_openAILock = new(1);
    private static readonly SemaphoreSlim s_embeddingLock = new(1);

    private static Task<MilvusEmbedService> GetMilvusEmbedService(AppOptions options) =>
        GetLazyClientAsync<MilvusEmbedService>(options, s_embeddingLock, async o =>
        {
            var milvusClient = new MilvusClient(options.milvusURL, username: options.milvusUsername, password: options.milvusPassword, port: options.milvusPort);

            var documentClient = await GetFormRecognizerClientAsync(o);
            var blobContainerClient = await GetBlobContainerClientAsync(o);
            var openAIClient = await GetOpenAIClientAsync(o);
            var embeddingModelName = o.EmbeddingModelName ?? throw new ArgumentNullException(nameof(o.EmbeddingModelName));

            return new MilvusEmbedService(
                openAIClient: openAIClient,
                milvusClient,
                embeddingModelName: embeddingModelName,
                documentAnalysisClient: documentClient,
                corpusContainerClient: blobContainerClient,
                logger: null);
        });

    private static Task<BlobContainerClient> GetBlobContainerClientAsync(AppOptions options) =>
        GetLazyClientAsync<BlobContainerClient>(options, s_containerLock, static async o =>
        {
            if (s_containerClient is null)
            {

                var blobService = new BlobServiceClient(
                    o.BlobConnectionString
                    );

                var blobContainerName = o.Container;
                ArgumentNullException.ThrowIfNullOrEmpty(blobContainerName);

                s_containerClient = blobService.GetBlobContainerClient(blobContainerName);

                await s_containerClient.CreateIfNotExistsAsync(PublicAccessType.BlobContainer);
            }

            return s_containerClient;
        });

    private static Task<DocumentAnalysisClient> GetFormRecognizerClientAsync(AppOptions options) =>
        GetLazyClientAsync<DocumentAnalysisClient>(options, s_documentLock, static async o =>
        {
            if (s_documentClient is null)
            {
                s_documentClient = new DocumentAnalysisClient(
                    new Uri(o.FormRecognizerServiceEndpoint),
                    new AzureKeyCredential(o.FormRecognizerServiceKey),
                    new DocumentAnalysisClientOptions
                    {
                        Diagnostics =
                        {
                            IsLoggingContentEnabled = true
                        }
                    });
            }

            await Task.CompletedTask;

            return s_documentClient;
        });

    private static Task<OpenAIClient> GetOpenAIClientAsync(AppOptions options) =>
       GetLazyClientAsync<OpenAIClient>(options, s_openAILock, async o =>
       {
           if (s_openAIClient is null)
           {
               s_openAIClient = new OpenAIClient(
                   new Uri(o.AzureOpenAIServiceEndpoint),
                   new AzureKeyCredential(o.AzureOpenAIServiceKey)
               );
           }
           await Task.CompletedTask;
           return s_openAIClient;
       });

    private static async Task<TClient> GetLazyClientAsync<TClient>(
        AppOptions options,
        SemaphoreSlim locker,
        Func<AppOptions, Task<TClient>> factory)
    {
        await locker.WaitAsync();

        try
        {
            return await factory(options);
        }
        finally
        {
            locker.Release();
        }
    }
}
