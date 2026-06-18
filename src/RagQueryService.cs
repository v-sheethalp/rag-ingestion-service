using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

#pragma warning disable SKEXP0001

public sealed class RagQueryService
{
    private readonly ITextEmbeddingGenerationService _embedder;
    private readonly SearchClient _search;
    private readonly Kernel _kernel;

    public RagQueryService(ITextEmbeddingGenerationService embedder, SearchClient search, Kernel kernel)
    {
        _embedder = embedder;
        _search = search;
        _kernel = kernel;
    }

    public async Task<string> AskAsync(string question, int k = 5, CancellationToken ct = default)
    {
        var qEmb = await _embedder.GenerateEmbeddingAsync(question, cancellationToken: ct);

        var options = new SearchOptions
        {
            Size = k,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(qEmb.ToArray())
                    {
                        KNearestNeighborsCount = k,
                        Fields = { "Embedding" }
                    }
                }
            }
        };

        var results = await _search.SearchAsync<DocChunk>(searchText: null, options, ct);

        var context = string.Join("\n---\n",
            results.Value.GetResults()
                .Select(r => $"[{r.Document.FilePath}]\n{r.Document.Content}"));

        if (string.IsNullOrWhiteSpace(context))
            return "No relevant content found in the index.";

        var prompt = $$"""
            You are a documentation assistant. Answer the question using ONLY the context.
            If the context does not contain the answer, say so. Cite the file paths you used.

            Context:
            {{context}}

            Question: {{question}}

            Answer:
            """;

        var resp = await _kernel.InvokePromptAsync(prompt, cancellationToken: ct);
        return resp.ToString();
    }
}
