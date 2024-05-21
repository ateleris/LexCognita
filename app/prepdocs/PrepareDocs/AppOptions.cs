// Copyright (c) Microsoft. All rights reserved.

using System.CommandLine;

namespace PrepareDocs;

internal record class AppOptions(
    string Files,
    string? Category,
    bool SkipBlobs,
    string? StorageServiceBlobEndpoint,
    string? Container,
    string? TenantId,
    string? SearchServiceEndpoint,
    string? AzureOpenAIServiceEndpoint,
    string? SearchIndexName,
    string? EmbeddingModelName,
    bool Remove,
    bool RemoveAll,
    string? FormRecognizerServiceEndpoint,
    string? ComputerVisionServiceEndpoint,
    bool Verbose,
    IConsole Console,
    string milvusURL,
    int milvusPort,
    string milvusUsername,
    string milvusPassword) : AppConsole(Console);

internal record class AppConsole(IConsole Console);
