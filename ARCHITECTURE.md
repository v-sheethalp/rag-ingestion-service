# RAG Ingestion Service - Architecture Deep Dive

## System Architecture

### High-Level Overview

The RAG Ingestion Service is a distributed system designed to:
1. Fetch documentation from Azure DevOps repositories
2. Process and chunk the content intelligently
3. Generate vector embeddings using Azure OpenAI
4. Store indexed documents in Azure AI Search
5. Enable semantic search with LLM-powered question answering

### Component Diagram

```
┌────────────────────────────────────────────────────────────┐
│                    EXTERNAL SERVICES                       │
├────────────────────────────────────────────────────────────┤
│  Azure DevOps Repos  │  Azure OpenAI  │  Azure AI Search   │
└────────────────────────────────────────────────────────────┘
                              ▲
                              │
                              │
┌────────────────────────────────────────────────────────────┐
│                   APPLICATION LAYER                        │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌──────────────────────┐       ┌──────────────────────┐ │
│  │ RagIngestionService  │       │  RagQueryService     │ │
│  │                      │       │                      │ │
│  │ • Fetch files        │       │ • Search queries     │ │
│  │ • Process markdown   │       │ • Generate answers   │ │
│  │ • Create chunks      │       │ • Citation support   │ │
│  │ • Embed texts        │       │                      │ │
│  │ • Index documents    │       │                      │ │
│  └──────────────────────┘       └──────────────────────┘ │
│                                                            │
├────────────────────────────────────────────────────────────┤
│                    DATA MODELS                             │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌────────────────────────────────────────────────────┐   │
│  │ DocChunk                                           │   │
│  │ • Id: string (unique identifier)                   │   │
│  │ • FilePath: string (source reference)             │   │
│  │ • Content: string (text chunk)                    │   │
│  │ • Embedding: float[] (1536-dim vector)            │   │
│  └────────────────────────────────────────────────────┘   │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

## Detailed Component Breakdown

### 1. RagIngestionService

**Responsibilities:**
- Repository tree traversal
- File filtering (markdown files only)
- Content fetching from Azure DevOps
- Markdown-to-plaintext conversion
- Intelligent text chunking
- Vector embedding generation
- Batch upload to Azure Search

**Key Methods:**
```csharp
public async Task EnsureIndexAsync(CancellationToken ct)
```
- Creates or updates the Azure AI Search index
- Configures HNSW vector search algorithm
- Sets up vector search profiles

```csharp
public async Task<int> IngestPathAsync(string path, string branch, CancellationToken ct)
```
- Main ingestion pipeline
- Returns total chunks ingested
- Robust error handling with logging

**Private Methods:**
```csharp
private static IEnumerable<string> Chunk(string text, int size, int overlap)
```
- Implements sliding window chunking
- Maintains semantic context with overlaps
- Memory-efficient streaming via yield

```csharp
private static string SanitizeId(string raw)
```
- Ensures Azure Search key validity
- Converts illegal characters to underscores
- Prepends 'd' to guarantee letter start

### 2. RagQueryService

**Responsibilities:**
- Query embedding generation
- Vector similarity search
- Context assembly from results
- LLM-powered answer generation
- Citation formatting

**Main Method:**
```csharp
public async Task<string> AskAsync(string question, int k = 5, CancellationToken ct)
```

**Process Flow:**
1. Embed user question to 1536-dim vector
2. Configure vector search options (k nearest neighbors)
3. Query Azure Search for top-k similar chunks
4. Format retrieved chunks as context (with file paths)
5. Create semantic kernel prompt with:
   - System role: Documentation assistant
   - Context: Retrieved document chunks
   - User question
6. Invoke LLM to generate answer
7. Return answer with citations

### 3. DocChunk Model

**Properties:**

| Property | Type | Purpose | Index Role |
|----------|------|---------|------------|
| `Id` | `string` | Unique key | Primary key (searchable field) |
| `FilePath` | `string` | Source reference | Filterable, searchable |
| `Content` | `string` | Text content | Searchable |
| `Embedding` | `float[]` | Vector representation | Vector search field (1536 dims) |

**Attributes:**
- `[SimpleField(IsKey = true)]`: Designates primary key
- `[SearchableField(IsFilterable = true)]`: Allows text search + filtering
- `[SearchableField]`: Enables full-text search
- `[VectorSearchField(...)]`: Enables vector similarity search

## Data Processing Pipeline

### Ingestion Pipeline

```
START: IngestPathAsync(path, branch)
  │
  ├─ STEP 1: Fetch Tree
  │  └─ Call AzureDevopsClient.ListTreeItemsAsync()
  │     └─ Returns JSON array of repository items
  │
  ├─ STEP 2: Filter & Download
  │  ├─ Filter for blob type (files, not directories)
  │  ├─ Filter for .md extension
  │  └─ Download file content using GetFileContentAsync()
  │
  ├─ STEP 3: Process Markdown
  │  ├─ Use Markdig to convert markdown to plain text
  │  ├─ Remove formatting, links, images, etc.
  │  └─ Output: plain text string
  │
  ├─ STEP 4: Chunk Text
  │  ├─ Use Chunk() method with:
  │  │  - ChunkSize: 1200 characters
  │  │  - ChunkOverlap: 200 characters
  │  │  - Step: max(1, 1200 - 200) = 1000
  │  ├─ Sliding window: positions 0, 1000, 2000, ...
  │  ├─ Example:
  │  │  Chunk 1: chars[0:1200]
  │  │  Chunk 2: chars[1000:2200]
  │  │  Chunk 3: chars[2000:3200]
  │  └─ Output: list of text chunks
  │
  ├─ STEP 5: Generate Embeddings (BATCH)
  │  ├─ Batch chunks: 64 chunks per Azure OpenAI call
  │  ├─ For chunks 0-63:
  │  │  └─ Call GenerateEmbeddingsAsync(chunks)
  │  │     └─ Returns list of 1536-dim vectors
  │  ├─ For chunks 64-127:
  │  │  └─ Call GenerateEmbeddingsAsync(chunks)
  │  └─ Continue for all chunks
  │
  ├─ STEP 6: Create DocChunk Objects
  │  ├─ For each (chunk, embedding) pair:
  │  │  ├─ Id = SanitizeId($"{filePath}_{index}")
  │  │  ├─ FilePath = original file path
  │  │  ├─ Content = chunk text
  │  │  └─ Embedding = vector from STEP 5
  │  └─ Add to batch list (max 50)
  │
  ├─ STEP 7: Upload to Azure Search (BATCH)
  │  ├─ When batch reaches 50:
  │  │  └─ Call UploadDocumentsAsync(batch)
  │  └─ When all files processed:
  │     └─ Upload remaining batch
  │
  └─ RETURN: Total count of chunks uploaded
```

### Query Pipeline

```
START: AskAsync(question, k=5)
  │
  ├─ STEP 1: Embed Question
  │  └─ Call GenerateEmbeddingAsync(question)
  │     └─ Returns 1536-dim vector
  │
  ├─ STEP 2: Configure Search
  │  ├─ Create SearchOptions:
  │  │  ├─ Size: k (default 5)
  │  │  ├─ VectorSearch.Queries:
  │  │  │  ├─ VectorizedQuery(question_embedding)
  │  │  │  ├─ KNearestNeighborsCount: k
  │  │  │  └─ Fields: ["Embedding"]
  │  │  └─ No text search (pure vector search)
  │  └─ Result: Configuration object
  │
  ├─ STEP 3: Execute Search
  │  └─ Call SearchAsync<DocChunk>(searchText: null, options, ct)
  │     └─ Returns SearchResults with top-k chunks
  │
  ├─ STEP 4: Extract Results
  │  ├─ For each result:
  │  │  ├─ Document.FilePath (e.g., "/docs/api.md")
  │  │  └─ Document.Content (chunk text)
  │  └─ Format:
  │     [/docs/api.md]
  │     <chunk text>
  │     ---
  │     [/docs/setup.md]
  │     <chunk text>
  │
  ├─ STEP 5: Build Prompt
  │  ├─ System role: "Documentation assistant"
  │  ├─ Instructions: "Answer using ONLY context"
  │  ├─ Context: Formatted chunks from STEP 4
  │  └─ Question: User's input question
  │
  ├─ STEP 6: Generate Answer
  │  └─ Call kernel.InvokePromptAsync(prompt)
  │     └─ Returns LLM-generated response
  │
  └─ RETURN: Answer string with citations
```

## Performance Optimizations

### Chunking Optimization
**Problem:** Single embedding per document loses local semantic detail
**Solution:** Overlapping chunks (1200 chars, 200 overlap)
**Benefit:** Better context preservation, improved search relevance

### Embedding Batching
**Problem:** 1 API call per chunk = high latency & cost
**Solution:** Batch 64 chunks per Azure OpenAI API call
**Benefit:** Reduces API calls by ~64x, maintains throughput

### Upload Batching
**Problem:** Individual document uploads = slow indexing
**Solution:** Batch 50 chunks per Azure Search upload
**Benefit:** Faster indexing, reduced network overhead

### Vector Search Algorithm
**Algorithm:** HNSW (Hierarchical Navigable Small World)
**Characteristics:**
- Approximate nearest neighbor (not exact)
- O(log n) search complexity
- Fast similarity search in high-dimensional spaces
- Trade-off: Speed vs perfect accuracy (acceptable for semantic search)

## Error Handling Strategy

### File-Level Errors
```csharp
try { md = await _ado.GetFileContentAsync(filePath, branch); }
catch (Exception ex) { _log.LogWarning(ex, "Skip {file}", filePath); continue; }
```
- Logs warning but continues processing
- Prevents one bad file from stopping entire ingestion

### Cancellation Support
```csharp
ct.ThrowIfCancellationRequested();
```
- Checks token in processing loop
- Allows graceful shutdown during long operations

### Logging
- WARNING: File-level failures
- INFORMATION: Ingestion completion with count
- Helps troubleshoot and monitor production

## Scalability Considerations

### Current Constraints
- **Embedding Batch Size:** 64 chunks (limited by Azure OpenAI API)
- **Upload Batch Size:** 50 chunks (optimized for Azure Search)
- **Memory:** Depends on chunk count before upload

### Potential Improvements
1. **Parallel File Processing:** Process multiple files concurrently
2. **Streaming Embeddings:** Stream embedding results instead of buffering
3. **Dynamic Batching:** Adjust batch sizes based on API quotas
4. **Incremental Ingestion:** Update index without full re-ingestion

## Security Considerations

### Current Implementation
- API credentials passed via dependency injection
- No secrets stored in code
- Support for CancellationToken for safe shutdown

### Recommendations
1. Use Azure Key Vault for credential management
2. Implement authentication for query service
3. Add audit logging for data access
4. Use managed identities where possible

## Monitoring & Observability

### Metrics to Track
- Chunks processed per ingestion
- Embedding generation time
- Search query latency
- Answer generation time
- Error rates by type

### Logging Levels
- ERROR: Critical failures (index creation, search failures)
- WARNING: Recoverable issues (missing files)
- INFORMATION: Operation summaries (ingestion count)
- DEBUG: Detailed processing steps (for troubleshooting)
