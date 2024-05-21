// Copyright (c) Microsoft. All rights reserved.

namespace PrepareDocs;

internal record class AppOptions(
    string Files,
    string? Category,
    bool SkipBlobs,
    string BlobConnectionString,
    string Container,
    string? TenantId,
    string? SearchServiceEndpoint,
    string AzureOpenAIServiceEndpoint,
    string AzureOpenAIServiceKey,
    string EmbeddingModelName,
    bool Remove,
    bool RemoveAll,
    string FormRecognizerServiceEndpoint,
    string FormRecognizerServiceKey,
    string? ComputerVisionServiceEndpoint,
    bool Verbose,
    string milvusURL,
    int milvusPort,
    string milvusUsername,
    string milvusPassword
);

