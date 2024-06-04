using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using MinimalApi.Extensions;
using Shared.Models;
using System.Text.Json;

namespace MinimalApi.Services;
#pragma warning disable SKEXP0011 // Mark members as static
#pragma warning disable SKEXP0001 // Mark members as static
public class ReadRetrieveReadChatService
{
    private readonly MilvusSearchService _searchClient;
    private readonly Kernel _kernel;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential? _tokenCredential;
    private readonly int _documentNumber;

    public ReadRetrieveReadChatService(
        MilvusSearchService searchClient,
        OpenAIClient client,
        IConfiguration configuration,
        TokenCredential? tokenCredential = null)
    {
        _searchClient = searchClient;
        var kernelBuilder = Kernel.CreateBuilder();

        _documentNumber = int.TryParse(configuration["DocumentNumber"], out int num) ? num : 5;

        var deployedModelName = configuration["AzureOpenAiChatGptDeployment"];
        ArgumentException.ThrowIfNullOrWhiteSpace(deployedModelName);
        var embeddingModelName = configuration["AzureOpenAiEmbeddingDeployment"];
        if (!string.IsNullOrEmpty(embeddingModelName))
        {
            var endpoint = configuration["AzureOpenAiServiceEndpoint"];
            var apiKey = configuration["AzureOpenAiServiceKey"];
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

            kernelBuilder = kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(embeddingModelName, endpoint, apiKey);
            kernelBuilder = kernelBuilder.AddAzureOpenAIChatCompletion(deployedModelName, endpoint, apiKey);
        }

        _kernel = kernelBuilder.Build();
        _configuration = configuration;
        _tokenCredential = tokenCredential;
    }

    public async Task<ChatAppResponse> ReplyAsync(
        ChatMessage[] history,
        RequestOverrides? overrides,
        CancellationToken cancellationToken = default)
    {
        var top = overrides?.Top ?? 3;
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;
        var useSemanticRanker = overrides?.SemanticRanker ?? false;
        var excludeCategory = overrides?.ExcludeCategory ?? null;
        var filter = excludeCategory is null ? null : $"category ne '{excludeCategory}'";
        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var embedding = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        float[]? embeddings = null;
        var question = history.LastOrDefault(m => m.IsUser)?.Content is { } userQuestion
            ? userQuestion
            : throw new InvalidOperationException("Use question is null");

        string[]? followUpQuestionList = null;
        if (overrides?.RetrievalMode != RetrievalMode.Text && embedding is not null)
        {
            embeddings = (await embedding.GenerateEmbeddingAsync(question, cancellationToken: cancellationToken)).ToArray();
        }

        // step 1
        // use llm to get query if retrieval mode is not vector
        string? query = null;
        if (overrides?.RetrievalMode != RetrievalMode.Vector)
        {
            var getQueryChat = new ChatHistory(@"You are a helpful legal AI assistant answering questions about the Apple vs Epic case. You interact with people who have a legal background. Be brief in your answer, generate search query for followup question.
Make your respond simple and precise. Return the query only, do not return any other text.
e.g.
gov.uscourts.cand.364265.1.0_2-0 AND gov.uscourts.cand.364265.1.0_2-1
AND gov.uscourts.cand.364265.1.0_2-10.
");

            getQueryChat.AddUserMessage(question);
            var result = await chat.GetChatMessageContentAsync(
                getQueryChat,
                cancellationToken: cancellationToken);

            query = result.Content ?? throw new InvalidOperationException("Failed to get search query");
        }

        // step 2
        // use query to search related docs
        var documentContentList = await _searchClient.QueryDocumentsAsync(query, embeddings, overrides, _documentNumber, cancellationToken);

        string documentContents = string.Empty;
        if (documentContentList.Length == 0)
        {
            documentContents = "no source available.";
        }
        else
        {
            documentContents = string.Join("\r", documentContentList.Select(x => $"{x.Title}:{x.Content}"));
        }

        // step 3
        // put together related docs and conversation history to generate answer
        var answerChat = new ChatHistory(
           "You are a helpful legal AI assistant answering questions about the Apple vs Epic case. You interact with people who have a legal background. Your answers are related to the Apple vs Epic legal case.");

        // add chat history
        foreach (var message in history)
        {
            if (message.IsUser)
            {
                answerChat.AddUserMessage(message.Content);
            }
            else
            {
                answerChat.AddAssistantMessage(message.Content);
            }
        }

        var prompt = @$" ## Source ##
{documentContents}
## End ##

Your answer needs to be a valid json object with the following format. Please escape all special characters and return the answers as valid json string.
{{
""answer"": // the answer to the question, add a source reference to the end of each sentence. e.g. Apple is a fruit [reference1.pdf][reference2.pdf]. If no source available elaborate why as answer.
""thoughts"": // brief thoughts on how you came up with the answer, e.g. what sources you used, what you thought about, etc.
}}";
        answerChat.AddUserMessage(prompt);


        var promptExecutingSetting = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = overrides?.Temperature ?? 0.7,
            StopSequences = [],

#pragma warning disable SKEXP0013 // Rethrow to preserve stack details
            ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
#pragma warning restore SKEXP0013
        };

        // get answer
        var answer = await chat.GetChatMessageContentAsync(
                       answerChat,
                       promptExecutingSetting,
                       cancellationToken: cancellationToken);

        var answerJson = answer.Content ?? throw new InvalidOperationException("Failed to get search query");
        //Console.WriteLine(answerJson);

        //// fix source links
        //ISet<string> presentCitations = new HashSet<string>();
        //foreach (var sourceDoc in documentContentList)
        //{
        //    if (answerJson.Contains(sourceDoc.Title))
        //    {
        //        presentCitations.Add(sourceDoc.Title);
        //    }
        //}
        //// remove citations
        //answerJson = Regex.Replace(answerJson, @"\[.*\]", "");

        JsonElement answerObject;
        try
        {
            answerObject = JsonSerializer.Deserialize<JsonElement>(answerJson);
        }
        catch (JsonException)
        {
            var resMsg = new ResponseMessage("assistant", "I'm sorry. I could not formulate a valid response.");
            var resCtx = new ResponseContext(
                DataPointsContent: [],
                DataPointsImages: [],
                FollowupQuestions: [],
                Thoughts: new[] { new Thoughts("Thoughts", "") });

            var resChc = new ResponseChoice(
                Index: 0,
                Message: resMsg,
                Context: resCtx,
                CitationBaseUrl: _configuration.ToCitationBaseUrl());

            return new ChatAppResponse([resChc]);
        }

        string ans = "";
        try
        {
            ans = answerObject.GetProperty("answer").GetString() ?? "";
        }
        catch (InvalidOperationException)
        {
            foreach (var item in answerObject.GetProperty("answer").EnumerateArray())
            {
                ans += item.GetString();
                ans += "\n";
            }
        }

        var thoughts = answerObject.GetProperty("thoughts").GetString() ?? throw new InvalidOperationException("Failed to get thoughts");

        //// readd citations
        //foreach (string citation in presentCitations)
        //{
        //    ans += $"[{citation}]";
        //}

        // step 4
        // add follow up questions if requested
        if (overrides?.SuggestFollowupQuestions is true)
        {
            var followUpQuestionChat = new ChatHistory(@"You are a helpful AI assistant");
            followUpQuestionChat.AddUserMessage($@"Generate three follow-up question based on the answer you just generated.
# Answer
{ans}

# Format of the response
Return the follow-up question as a json string list. Don't put your answer between ```json and ```, return the json string directly.
e.g.
{{
    ""followUpQuestions"": [
        ""What is the deductible?"",
        ""What is the co-pay?"",
        ""What is the out-of-pocket maximum?""
    ]
}}");

            var followUpQuestions = await chat.GetChatMessageContentAsync(
                followUpQuestionChat,
                promptExecutingSetting,
                cancellationToken: cancellationToken);

            var followUpQuestionsJson = followUpQuestions.Content ?? throw new InvalidOperationException("Failed to get search query");
            var followUpQuestionsObject = JsonSerializer.Deserialize<JsonElement>(followUpQuestionsJson);
            var followUpQuestionsList = followUpQuestionsObject.GetProperty("followUpQuestions").EnumerateArray().Select(x => x.GetString()!).ToList();
            foreach (var followUpQuestion in followUpQuestionsList)
            {
                ans += $" <<{followUpQuestion}>> ";
            }

            followUpQuestionList = followUpQuestionsList.ToArray();
        }

        var responseMessage = new ResponseMessage("assistant", ans);
        var responseContext = new ResponseContext(
            DataPointsContent: documentContentList.Select(x => new SupportingContentRecord(x.Title, x.Content)).ToArray(),
            DataPointsImages: [],
            FollowupQuestions: followUpQuestionList ?? Array.Empty<string>(),
            Thoughts: new[] { new Thoughts("Thoughts", thoughts) });

        var choice = new ResponseChoice(
            Index: 0,
            Message: responseMessage,
            Context: responseContext,
            CitationBaseUrl: _configuration.ToCitationBaseUrl());

        return new ChatAppResponse([choice]);
    }
}
