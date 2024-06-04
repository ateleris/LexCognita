using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EmbedFunctions.Services;
using Microsoft.Extensions.FileSystemGlobbing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PrepareDocs;
using System.Text.RegularExpressions;

var options = new AppOptions(
    "C:\\TEMP\\EpicVSAppleData",
    null,
    SkipBlobs: false,
    BlobConnectionString: "",
    Container: "epicvsapple",
    null,
    null,
    AzureOpenAIServiceEndpoint: "https://lexcognitai.openai.azure.com/",
    AzureOpenAIServiceKey: "",
    EmbeddingModelName: "text-embedding-ada-002",
    false,
    "https://lexcognitadi.cognitiveservices.azure.com/",
    "",
    null,
    true,
    "localhost",
    19530,
    "default",
    "default"
);

var embedService = await GetMilvusEmbedService(options);

Matcher matcher = new();
// From bash, the single quotes surrounding the path (to avoid expansion of the wildcard), are included in the argument value.
var files = Directory.GetFiles(options.Files);

Console.WriteLine($"Processing {files.Length} files...");

var tasks = Enumerable.Range(0, files.Length)
    .Select(i =>
    {
        var fileName = files[i];
        return ProcessSingleFileAsync(options, fileName, embedService);
    });

await Task.WhenAll(tasks);

static async Task ProcessSingleFileAsync(AppOptions options, string fileName, MilvusEmbedService embedService)
{
    if (options.Verbose)
    {
        Console.WriteLine($"Processing '{fileName}'");
    }

    if (options.Remove)
    {
        await RemoveBlobsAsync(options, fileName);
        await RemoveFromIndexAsync(options, fileName);
        return;
    }

    if (options.SkipBlobs)
    {
        return;
    }

    await UploadBlobsAndCreateIndexAsync(options, fileName, embedService);
}

static async ValueTask RemoveBlobsAsync(
    AppOptions options, string? fileName = null)
{
    if (options.Verbose)
    {
        Console.WriteLine($"Removing blobs for '{fileName ?? "all"}'");
    }

    var prefix = string.IsNullOrWhiteSpace(fileName)
        ? Path.GetFileName(fileName)
        : null;

    var getContainerClientTask = GetBlobContainerClientAsync(options);
    var clientTasks = new[] { getContainerClientTask };

    await Task.WhenAll(clientTasks);

    foreach (var clientTask in clientTasks)
    {
        var client = await clientTask;
        await DeleteAllBlobsFromContainerAsync(client, prefix);
    }

    static async Task DeleteAllBlobsFromContainerAsync(BlobContainerClient client, string? prefix)
    {
        await foreach (var blob in client.GetBlobsAsync())
        {
            if (string.IsNullOrWhiteSpace(prefix) ||
                blob.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await client.DeleteBlobAsync(blob.Name);
            }
        }
    };
}

static async ValueTask RemoveFromIndexAsync(
    AppOptions options, string? fileName = null)
{
    throw new NotImplementedException();
}

static async ValueTask UploadBlobsAndCreateIndexAsync(
    AppOptions options, string fileName, MilvusEmbedService embeddingService)
{
    var container = await GetBlobContainerClientAsync(options);

    // If it's a PDF, split it into single pages.
    if (Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            using var documents = PdfReader.Open(fileName, PdfDocumentOpenMode.Import);

            for (int i = 0; i < documents.PageCount; i++)
            {
                var documentName = BlobNameFromFilePage(fileName, i);
                var blobClient = container.GetBlobClient(documentName);
                if (await blobClient.ExistsAsync())
                {
                    continue;
                }

                var tempFileName = Path.GetTempFileName();

                try
                {
                    using var document = new PdfDocument();
                    document.AddPage(documents.Pages[i]);
                    document.Save(tempFileName);

                    await using var stream = File.OpenRead(tempFileName);
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders
                    {
                        ContentType = "application/pdf"
                    });

                    // revert stream position
                    stream.Position = 0;

                    await embeddingService.EmbedPDFBlobAsync(stream, documentName);
                }
                finally
                {
                    File.Delete(tempFileName);
                }
                Console.WriteLine($"Finished '{documentName}'");
            }
        }
        catch (PdfReaderException)
        {
            Console.WriteLine($"ERROR WITH DOCUMENT '{fileName}'");
        }

    }
    else
    {
        throw new NotImplementedException();
    }
}

static string BlobNameFromFilePage(string filename, int page = 0) => Path.GetExtension(filename).ToLower() is ".pdf"
        ? $"{Path.GetFileNameWithoutExtension(filename)}-{page}.pdf"
        : Path.GetFileName(filename);

internal static partial class Program
{
    [GeneratedRegex("[^0-9a-zA-Z_-]")]
    private static partial Regex MatchInSetRegex();

    internal static DefaultAzureCredential DefaultCredential { get; } = new();
}
