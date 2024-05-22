using Milvus.Client;
using Shared.Models;
using Shared.Services;

public class MilvusSearchService(MilvusClient milvusClient) : MilvusBase(milvusClient, null, null)
{
    public async Task<SupportingContentRecord[]> QueryDocumentsAsync(
        string? query = null,
        float[]? embedding = null,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        if (query is null && embedding is null)
        {
            throw new ArgumentException("Either query or embedding must be provided");
        }

        var documentContents = string.Empty;
        var top = overrides?.Top ?? 3;
        var exclude_category = overrides?.ExcludeCategory;
        var filter = exclude_category == null ? string.Empty : $"category ne '{exclude_category}'";
        var useSemanticRanker = overrides?.SemanticRanker ?? false;
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;

        var collection = await GetOrCreateCollectionAsync();

        await collection.LoadAsync();

        var parameters = new SearchParameters
        {
            OutputFields = { "content", "sourcepage" },
            ConsistencyLevel = ConsistencyLevel.Strong,
            ExtraParameters = { ["nprobe"] = "1024" }
        };

        List<ReadOnlyMemory<float>> embeds = [];
        embeds.Add(embedding);

        var results = await collection.SearchAsync(
            vectorFieldName: "embedding",
            vectors: embeds,
            SimilarityMetricType.L2,
            limit: 3,
            parameters
        );

        // Assemble sources here.
        // Example output for each SearchDocument:
        // {
        //   "@search.score": 11.65396,
        //   "id": "Northwind_Standard_Benefits_Details_pdf-60",
        //   "content": "x-ray, lab, or imaging service, you will likely be responsible for paying a copayment or coinsurance. The exact amount you will be required to pay will depend on the type of service you receive. You can use the Northwind app or website to look up the cost of a particular service before you receive it.\nIn some cases, the Northwind Standard plan may exclude certain diagnostic x-ray, lab, and imaging services. For example, the plan does not cover any services related to cosmetic treatments or procedures. Additionally, the plan does not cover any services for which no diagnosis is provided.\nIt’s important to note that the Northwind Standard plan does not cover any services related to emergency care. This includes diagnostic x-ray, lab, and imaging services that are needed to diagnose an emergency condition. If you have an emergency condition, you will need to seek care at an emergency room or urgent care facility.\nFinally, if you receive diagnostic x-ray, lab, or imaging services from an out-of-network provider, you may be required to pay the full cost of the service. To ensure that you are receiving services from an in-network provider, you can use the Northwind provider search ",
        //   "category": null,
        //   "sourcepage": "Northwind_Standard_Benefits_Details-24.pdf",
        //   "sourcefile": "Northwind_Standard_Benefits_Details.pdf"
        // }
        var sb = new List<SupportingContentRecord>();

        // todo retrieve data from results
        for (int i = 0; i < results.FieldsData[0].RowCount; i++)
        {
            var fd1 = (FieldData<string>)(results.FieldsData[0]);
            var fd2 = (FieldData<string>)(results.FieldsData[1]);

            var title = "";
            var content = "";

            if (fd1.FieldName == "content")
            {
                content = fd1.Data[i];
                title = fd2.Data[i];
            }
            else
            {
                content = fd2.Data[i];
                title = fd1.Data[i];
            }
            content = content.Replace('\r', ' ').Replace('\n', ' ');
            sb.Add(new SupportingContentRecord(title, content));
        }

        return [.. sb];
    }
}
