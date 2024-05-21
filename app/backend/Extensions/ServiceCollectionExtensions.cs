

using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Milvus.Client;
using MinimalApi.Services;

namespace MinimalApi.Extensions;

internal static class ServiceCollectionExtensions
{

    internal static IServiceCollection AddAzureServices(this IServiceCollection services)
    {
        services.AddSingleton<BlobServiceClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var blobStorageConnectionString = config["AzureBlobStorageConnectionString"];
            ArgumentNullException.ThrowIfNullOrEmpty(blobStorageConnectionString);

            var blobServiceClient = new BlobServiceClient(blobStorageConnectionString);

            return blobServiceClient;
        });

        services.AddSingleton<BlobContainerClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var azureStorageContainer = config["AzureStorageContainer"];
            return sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(azureStorageContainer);
        });

        services.AddSingleton<MilvusSearchService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();

            var url = config["Milvus:DBUrl"];
            var port = int.TryParse(config["Milvus:Port"], out int portNum) ? portNum : 19530;
            var username = config["Milvus:Username"];
            var password = config["Milvus:Password"];

            ArgumentException.ThrowIfNullOrEmpty(url);
            ArgumentException.ThrowIfNullOrEmpty(username);
            ArgumentException.ThrowIfNullOrEmpty(password);

            var client = new MilvusClient(url, username: username, password: password, port: port);

            return new MilvusSearchService(client);
        });

        services.AddSingleton<DocumentAnalysisClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var azureOpenAiServiceEndpoint = config["AzureOpenAiServiceEndpoint"] ?? throw new ArgumentNullException();
            var azureOpenAiServiceKey = config["AzureOpenAiServiceKey"] ?? throw new ArgumentNullException();

            var documentAnalysisClient = new DocumentAnalysisClient(
                new Uri(azureOpenAiServiceEndpoint),
                new AzureKeyCredential(azureOpenAiServiceKey)
            );
            return documentAnalysisClient;
        });

        services.AddSingleton<OpenAIClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();

            var azureOpenAiServiceEndpoint = config["AzureOpenAiServiceEndpoint"];
            var azureOpenAiServiceKey = config["AzureOpenAiServiceKey"];
            ArgumentNullException.ThrowIfNullOrEmpty(azureOpenAiServiceEndpoint);
            ArgumentNullException.ThrowIfNullOrEmpty(azureOpenAiServiceKey);

            var openAIClient = new OpenAIClient(
               new Uri(azureOpenAiServiceEndpoint),
               new AzureKeyCredential(azureOpenAiServiceKey)
            );

            return openAIClient;
        });

        services.AddSingleton<AzureBlobStorageService>();
        services.AddSingleton<ReadRetrieveReadChatService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var openAIClient = sp.GetRequiredService<OpenAIClient>();
            var searchClient = sp.GetRequiredService<MilvusSearchService>();
            return new ReadRetrieveReadChatService(searchClient, openAIClient, config);
        });

        return services;
    }

    internal static IServiceCollection AddCrossOriginResourceSharing(this IServiceCollection services)
    {
        services.AddCors(
            options =>
                options.AddDefaultPolicy(
                    policy =>
                        policy.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod()));

        return services;
    }
}
