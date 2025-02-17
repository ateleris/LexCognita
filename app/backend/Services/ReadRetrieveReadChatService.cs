﻿// Copyright (c) Microsoft. All rights reserved.

using System.Text.RegularExpressions;

namespace MinimalApi.Services;

public class ReadRetrieveReadChatService
{
    private readonly SearchClient _searchClient;
    private readonly IKernel _kernel;
    private readonly IConfiguration _configuration;

    public ReadRetrieveReadChatService(
        SearchClient searchClient,
        OpenAIClient client,
        IConfiguration configuration)
    {
        _searchClient = searchClient;
        var deployedModelName = configuration["AzureOpenAiChatGptDeployment"];
        ArgumentNullException.ThrowIfNullOrWhiteSpace(deployedModelName);

        var kernelBuilder = Kernel.Builder.WithAzureChatCompletionService(deployedModelName, client);
        var embeddingModelName = configuration["AzureOpenAiEmbeddingDeployment"];
        if (!string.IsNullOrEmpty(embeddingModelName))
        {
            var endpoint = configuration["AzureOpenAiServiceEndpoint"];
            ArgumentNullException.ThrowIfNullOrWhiteSpace(endpoint);
            kernelBuilder = kernelBuilder.WithAzureTextEmbeddingGenerationService(embeddingModelName, endpoint, new DefaultAzureCredential());
        }
        _kernel = kernelBuilder.Build();
        _configuration = configuration;
    }

    public async Task<ApproachResponse> ReplyAsync(
        ChatTurn[] history,
        RequestOverrides? overrides,
        CancellationToken cancellationToken = default)
    {
        var top = overrides?.Top ?? 3;
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;
        var useSemanticRanker = overrides?.SemanticRanker ?? false;
        var excludeCategory = overrides?.ExcludeCategory ?? null;
        var filter = excludeCategory is null ? null : $"category ne '{excludeCategory}'";
        IChatCompletion chat = _kernel.GetService<IChatCompletion>();
        ITextEmbeddingGeneration? embedding = _kernel.GetService<ITextEmbeddingGeneration>();
        float[]? embeddings = null;
        var question = history.LastOrDefault()?.User is { } userQuestion
            ? userQuestion
            : throw new InvalidOperationException("Use question is null");
        if (overrides?.RetrievalMode != "Text" && embedding is not null)
        {
            embeddings = (await embedding.GenerateEmbeddingAsync(question, cancellationToken: cancellationToken)).ToArray();
        }

        // step 1
        // use llm to get query if retrieval mode is not vector
        string? query = null;
        if (overrides?.RetrievalMode != "Vector")
        {
            var getQueryChat = chat.CreateNewChat(@"You are a helpful legal AI assistant answering questions about the Apple vs Epic case. You interact with people who have a legal background. Be brief in your answer, generate search query for followup question.
Make your respond simple and precise. Return the query only, do not return any other text.
e.g.
gov.uscourts.cand.364265.1.0_2-0 AND gov.uscourts.cand.364265.1.0_2-1
AND gov.uscourts.cand.364265.1.0_2-10.
");

            getQueryChat.AddUserMessage(question);
            var result = await chat.GetChatCompletionsAsync(
                getQueryChat,
                cancellationToken: cancellationToken);

            if (result.Count != 1)
            {
                throw new InvalidOperationException("Failed to get search query");
            }

            query = result[0].ModelResult.GetOpenAIChatResult().Choice.Message.Content;
        }

        // step 2
        // use query to search related docs
        var documentContentList = await _searchClient.QueryDocumentsAsync(query, embeddings, overrides, cancellationToken);

        string documentContents = string.Empty;
        if (documentContentList.Length == 0)
        {
            documentContents = "no source available.";
        }
        else
        {
            documentContents = string.Join("\r", documentContentList.Select(x => $"{x.Title}:{x.Content}"));
        }

        Console.WriteLine(documentContents);
        // step 3
        // put together related docs and conversation history to generate answer
        var answerChat = chat.CreateNewChat(
           "You are a helpful legal AI assistant answering questions about the Apple vs Epic case. You interact with people who have a legal background. Your answers are related to the Apple vs Epic legal case. Be brief in your answer.");

        // add chat history
        foreach (var turn in history)
        {
            answerChat.AddUserMessage(turn.User);
            if (turn.Bot is { } botMessage)
            {
                answerChat.AddAssistantMessage(botMessage);
            }
        }

        // format prompt
        answerChat.AddUserMessage(@$" ## Source ##
{documentContents}
## End ##

Your answer needs to be a valid json object with the following format and escaped special characters.
{{
    ""answer"": // the answer to the question, add a source reference to the end of each sentence. e.g. Apple is a fruit [reference1.pdf][reference2.pdf]. If no source available, put the answer as I don't know.
    ""thoughts"": // brief thoughts on how you came up with the answer, e.g. what sources you used, what you thought about, etc.
}}");

        // get answer
        var answer = await chat.GetChatCompletionsAsync(
                       answerChat,
                       cancellationToken: cancellationToken);
        string answerJson = answer[0].ModelResult.GetOpenAIChatResult().Choice.Message.Content;

        Console.WriteLine(answerJson);

        // fix source links
        IList<string> presentCitations = new List<string>();
        foreach (var sourceDoc in documentContentList)
        {
            if (answerJson.Contains(sourceDoc.Title))
            {
                presentCitations.Add(sourceDoc.Title);
            }
        }
        // remove citations
        answerJson = Regex.Replace(answerJson, @"\[.*\]", "");

        JsonElement answerObject;
        try
        {
            answerObject = JsonSerializer.Deserialize<JsonElement>(answerJson);
        }
        catch (JsonException)
        {
            return new ApproachResponse(
                DataPoints: documentContentList,
                Answer: "I'm sorry. I could not formulate a valid response.",
                Thoughts: "",
                CitationBaseUrl: _configuration.ToCitationBaseUrl()
            );
        }

        var ans = answerObject.GetProperty("answer").GetString() ?? throw new InvalidOperationException("Failed to get answer");
        var thoughts = answerObject.GetProperty("thoughts").GetString() ?? throw new InvalidOperationException("Failed to get thoughts");

        // readd citations
        foreach (string citation in presentCitations)
        {
            ans += $"[{citation}]";
        }

        // step 4
        // add follow up questions if requested
        if (overrides?.SuggestFollowupQuestions is true)
        {
            var followUpQuestionChat = chat.CreateNewChat(@"You are a helpful legal AI assistant");
            followUpQuestionChat.AddUserMessage($@"Generate three follow-up question based on the answer you just generated.
# Answer
{ans}

# Format of the response
Return the follow-up question as a json string list.
e.g.
[
    ""What is the deductible?"",
    ""What is the co-pay?"",
    ""What is the out-of-pocket maximum?""
]");

            var followUpQuestions = await chat.GetChatCompletionsAsync(
                followUpQuestionChat,
                cancellationToken: cancellationToken);

            var followUpQuestionsJson = followUpQuestions[0].ModelResult.GetOpenAIChatResult().Choice.Message.Content;
            var followUpQuestionsObject = JsonSerializer.Deserialize<JsonElement>(followUpQuestionsJson);
            var followUpQuestionsList = followUpQuestionsObject.EnumerateArray().Select(x => x.GetString()).ToList();
            foreach (var followUpQuestion in followUpQuestionsList)
            {
                ans += $" <<{followUpQuestion}>> ";
            }
        }
        return new ApproachResponse(
            DataPoints: documentContentList,
            Answer: ans,
            Thoughts: thoughts,
            CitationBaseUrl: _configuration.ToCitationBaseUrl());
    }
}
