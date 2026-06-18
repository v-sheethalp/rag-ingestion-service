using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Markdig;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.Logging;

#pragma warning disable SKEXP0001 // Embedding API is experimental

public sealed class RagIngestionService
{
    public const string IndexName = "ado-wiki";
    private const int ChunkSize = 1200;
    private const int ChunkOverlap = 200;

    private readonly AzureDevopsClient _ado;
    private readonly ITextEmbeddingGenerationService _embedder;
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _search;
    private readonly ILogger<RagIngestionService> _log;

    public RagIngestionService(
        AzureDevopsClient ado,
        ITextEmbeddingGenerationService embedder,
        SearchIndexClient indexClient,
        SearchClient search,
        ILogger<RagIngestionService> log)
    {
        _ado = ado;
        _embedder = embedder;
        _indexClient = indexClient;
        _search = search;
        _log = log;
    }

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var fields = new FieldBuilder().Build(typeof(DocChunk));

        var index = new SearchIndex(IndexName, fields)
        {
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hnsw") },
                Profiles = { new VectorSearchProfile("vec-profile", "hnsw") }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
    }

    public async Task<int> IngestPathAsync(string path, string branch, CancellationToken ct = default)
    {
        var tree = await _ado.ListTreeItemsAsync(path, "Full", branch);
        if (!tree.RootElement.TryGetProperty("value", out var items))
            return 0;

        int uploaded = 0;
        var batch = new List<DocChunk>(50);

        foreach (var item in items.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();

            if (!item.TryGetProperty("gitObjectType", out var t) || t.GetString() != "blob") continue;
            if (!item.TryGetProperty("path", out var p)) continue;

            var filePath = p.GetString();
            if (string.IsNullOrWhiteSpace(filePath) ||
                !filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            string md;
            try { md = await _ado.GetFileContentAsync(filePath, branch); }
            catch (Exception ex) { _log.LogWarning(ex, "Skip {file}", filePath); continue; }

            var plain = Markdown.ToPlainText(md);
            var chunks = Chunk(plain, ChunkSize, ChunkOverlap).ToList();
            if (chunks.Count == 0) continue;

            // Batch-embed up to 64 chunks per AOAI call
            const int embedBatch = 64;
            for (int i = 0; i < chunks.Count; i += embedBatch)
            {
                var slice = chunks.GetRange(i, Math.Min(embedBatch, chunks.Count - i));
                var embeddings = await _embedder.GenerateEmbeddingsAsync(slice, cancellationToken: ct);

                for (int j = 0; j < slice.Count; j++)
                {
                    batch.Add(new DocChunk
                    {
                        Id = SanitizeId($"{filePath}_{i + j}"),
                        FilePath = filePath!,
                        Content = slice[j],
                        Embedding = embeddings[j].ToArray()
                    });

                    if (batch.Count >= 50)
                    {
                        await _search.UploadDocumentsAsync(batch, cancellationToken: ct);
                        uploaded += batch.Count;
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Count > 0)
        {
            await _search.UploadDocumentsAsync(batch, cancellationToken: ct);
            uploaded += batch.Count;
        }

        _log.LogInformation("Ingested {count} chunks from {path}", uploaded, path);
        return uploaded;
    }

    private static IEnumerable<string> Chunk(string text, int size, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        int step = Math.Max(1, size - overlap);

        for (int i = 0; i < text.Length; i += step)
        {
            int len = Math.Min(size, text.Length - i);
            yield return text.Substring(i, len);
            if (i + len >= text.Length) yield break;
        }
    }
    private static string SanitizeId(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length + 1);
        sb.Append('d'); // ensure key starts with a letter
        foreach (var c in raw)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }
}
