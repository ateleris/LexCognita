

using Azure.AI.OpenAI;
using Milvus.Client;

var host = new HostBuilder()
    .ConfigureServices(services =>
    {
        var credential = new DefaultAzureCredential();

        static Uri GetUriFromEnvironment(string variable) => Environment.GetEnvironmentVariable(variable) is string value &&
                Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                uri is not null
                ? uri
                : throw new ArgumentException(
                $"Unable to parse URI from environment variable: {variable}");

        services.AddAzureClients(builder =>
        {
            builder.AddDocumentAnalysisClient(
                GetUriFromEnvironment("AZURE_FORMRECOGNIZER_SERVICE_ENDPOINT"));
        });


        services.AddSingleton<BlobContainerClient>(_ =>
        {
            var blobServiceClient = new BlobServiceClient(
                GetUriFromEnvironment("AZURE_STORAGE_BLOB_ENDPOINT"),
                credential);

            var containerClient = blobServiceClient.GetBlobContainerClient("corpus");

            containerClient.CreateIfNotExists();

            return containerClient;
        });

        services.AddSingleton<BlobServiceClient>(_ =>
        {
            return new BlobServiceClient(
                GetUriFromEnvironment("AZURE_STORAGE_BLOB_ENDPOINT"), credential);
        });

        services.AddSingleton<EmbeddingAggregateService>();

        services.AddSingleton<MilvusEmbedService>(provider =>
        {
            var useAOAI = Environment.GetEnvironmentVariable("USE_AOAI")?.ToLower() == "true";

            OpenAIClient? openAIClient = null;
            string? embeddingModelName = null;

            if (useAOAI)
            {
                var openaiEndPoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new ArgumentNullException("AZURE_OPENAI_ENDPOINT is null");
                embeddingModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? throw new ArgumentNullException("AZURE_OPENAI_EMBEDDING_DEPLOYMENT is null");
                openAIClient = new OpenAIClient(new Uri(openaiEndPoint), new DefaultAzureCredential());
            }
            else
            {
                embeddingModelName = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT") ?? throw new ArgumentNullException("OPENAI_EMBEDDING_DEPLOYMENT is null");
                var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new ArgumentNullException("OPENAI_API_KEY is null");
                openAIClient = new OpenAIClient(openaiKey);
            }

            var corpusContainer = provider.GetRequiredService<BlobContainerClient>();
            var documentClient = provider.GetRequiredService<DocumentAnalysisClient>();
            var logger = provider.GetRequiredService<ILogger<MilvusEmbedService>>();

            var url = Environment.GetEnvironmentVariable("MILVUS_CONNECTION_URL") ?? "localhost";
            var port = int.TryParse(Environment.GetEnvironmentVariable("MILVUS_CONNECTION_PORT"), out int portNum) ? portNum : 19530;
            var username = Environment.GetEnvironmentVariable("MILVUS_CONNECTION_USERNAME") ?? "default";
            var password = Environment.GetEnvironmentVariable("MILVUS_CONNECTION_PASSWORD") ?? "default";
            var milvusClient = new MilvusClient(url, username: username, password: password, port: port);

            return new MilvusEmbedService(
            openAIClient: openAIClient,
            milvusClient,
            embeddingModelName: embeddingModelName,
            documentAnalysisClient: documentClient,
            corpusContainerClient: corpusContainer,
            logger: logger);
        });
    })
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
