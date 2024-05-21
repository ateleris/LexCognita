// Copyright (c) Microsoft. All rights reserved.

using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
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
            var blobContainerClient = await GetCorpusBlobContainerClientAsync(o);
            var openAIClient = await GetOpenAIClientAsync(o);
            var embeddingModelName = o.EmbeddingModelName ?? throw new ArgumentNullException(nameof(o.EmbeddingModelName));
            var searchIndexName = o.SearchIndexName ?? throw new ArgumentNullException(nameof(o.SearchIndexName));
            var computerVisionService = await GetComputerVisionServiceAsync(o);

            return new MilvusEmbedService(
                openAIClient: openAIClient,
                milvusClient: milvusClient,
                embeddingModelName: embeddingModelName,
                searchIndexName: searchIndexName,
                documentAnalysisClient: documentClient,
                corpusContainerClient: blobContainerClient,
                logger: null);
        });

    private static Task<BlobContainerClient> GetCorpusBlobContainerClientAsync(AppOptions options) =>
        GetLazyClientAsync<BlobContainerClient>(options, s_corpusContainerLock, static async o =>
        {
            if (s_corpusContainerClient is null)
            {
                var endpoint = o.StorageServiceBlobEndpoint;
                ArgumentNullException.ThrowIfNullOrEmpty(endpoint);

                var blobService = new BlobServiceClient(
                    new Uri(endpoint),
                    DefaultCredential);

                s_corpusContainerClient = blobService.GetBlobContainerClient("corpus");

                await s_corpusContainerClient.CreateIfNotExistsAsync();
            }

            return s_corpusContainerClient;
        });

    private static Task<BlobContainerClient> GetBlobContainerClientAsync(AppOptions options) =>
        GetLazyClientAsync<BlobContainerClient>(options, s_containerLock, static async o =>
        {
            if (s_containerClient is null)
            {
                var endpoint = o.StorageServiceBlobEndpoint;
                ArgumentNullException.ThrowIfNullOrEmpty(endpoint);

                var blobService = new BlobServiceClient(
                    new Uri(endpoint),
                    DefaultCredential);

                var blobContainerName = o.Container;
                ArgumentNullException.ThrowIfNullOrEmpty(blobContainerName);

                s_containerClient = blobService.GetBlobContainerClient(blobContainerName);

                await s_containerClient.CreateIfNotExistsAsync();
            }

            return s_containerClient;
        });

    private static Task<DocumentAnalysisClient> GetFormRecognizerClientAsync(AppOptions options) =>
        GetLazyClientAsync<DocumentAnalysisClient>(options, s_documentLock, static async o =>
        {
            if (s_documentClient is null)
            {
                var endpoint = o.FormRecognizerServiceEndpoint;
                ArgumentNullException.ThrowIfNullOrEmpty(endpoint);

                s_documentClient = new DocumentAnalysisClient(
                    new Uri(endpoint),
                    DefaultCredential,
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

    private static Task<IComputerVisionService?> GetComputerVisionServiceAsync(AppOptions options) =>
        GetLazyClientAsync<IComputerVisionService?>(options, s_openAILock, async o =>
        {
            await Task.CompletedTask;
            var endpoint = o.ComputerVisionServiceEndpoint;

            if (string.IsNullOrEmpty(endpoint))
            {
                return null;
            }

            return new AzureComputerVisionService(new HttpClient(), endpoint, DefaultCredential);
        });

    private static Task<OpenAIClient> GetOpenAIClientAsync(AppOptions options) =>
       GetLazyClientAsync<OpenAIClient>(options, s_openAILock, async o =>
       {
           if (s_openAIClient is null)
           {
               var useAOAI = Environment.GetEnvironmentVariable("USE_AOAI") == "true";
               if (!useAOAI)
               {
                   var openAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                   Console.WriteLine("useAOAI value is: " + useAOAI.ToString());
                   ArgumentNullException.ThrowIfNullOrEmpty(openAIApiKey);
                   s_openAIClient = new OpenAIClient(openAIApiKey);
               }
               else
               {
                   var endpoint = o.AzureOpenAIServiceEndpoint;
                   ArgumentNullException.ThrowIfNullOrEmpty(endpoint);
                   s_openAIClient = new OpenAIClient(
                       new Uri(endpoint),
                       DefaultCredential);
               }
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
