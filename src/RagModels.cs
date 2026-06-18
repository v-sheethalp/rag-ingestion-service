using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

public sealed class DocChunk
{
    [SimpleField(IsKey = true)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [SearchableField(IsFilterable = true)]
    public string FilePath { get; set; } = "";

    [SearchableField]
    public string Content { get; set; } = "";

    [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "vec-profile")]
    public IReadOnlyList<float> Embedding { get; set; } = Array.Empty<float>();
}
