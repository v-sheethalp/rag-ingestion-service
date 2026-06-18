# RAG Ingestion Service - Documentation

## Project Overview

The **RAG Ingestion Service** is a Retrieval-Augmented Generation (RAG) system that ingests markdown documentation from Azure DevOps repositories, processes them into embeddings, and enables semantic search capabilities using Azure AI Search and Microsoft Semantic Kernel.

This system bridges Azure DevOps wiki content with advanced AI-powered search and question-answering capabilities, allowing users to semantically search documentation and get intelligent answers based on the indexed content.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Components](#components)
3. [Features](#features)
4. [Data Flow](#data-flow)
5. [Configuration & Setup](#configuration--setup)
6. [API Reference](#api-reference)
7. [Usage Examples](#usage-examples)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         RAG INGESTION SERVICE SYSTEM                        │
└─────────────────────────────────────────────────────────────────────────────┘

                            ┌──────────────────┐
                            │  Azure DevOps    │
                            │   Repository     │
                            │    (Wiki/Docs)   │
                            └────────┬─────────┘
                                     │
                                     │ (Fetch markdown files)
                                     ▼
                    ┌────────────────────────────────────┐
                    │   RagIngestionService              │
                    │  ┌──────────────────────────────┐  │
                    │  │ 1. Fetch Content from ADO    │  │
                    │  │ 2. Convert MD to Plain Text  │  │
                    │  │ 3. Chunk into Segments       │  │
                    │  │ 4. Generate Embeddings       │  │
                    │  │ 5. Upload to Search Index    │  │
                    │  └──────────────────────────────┘  │
                    └────────────────────────────────────┘
                                     │
                   ┌─────────────────┼─────────────────┐
                   │                 │                 │
                   ▼                 ▼                 ▼
        ┌──────────────────┐  ┌──────────────┐  ┌─────────────────┐
        │ AzureDevopsClient│  │ Embedder     │  │ Azure AI Search │
        │  (Fetch files)   │  │ (AOAI API)   │  │   (Vector Index)│
        └──────────────────┘  └──────────────┘  └─────────────────┘
                                                          │
                                                          │
                                                          ▼
                    ┌────────────────────────────────────────────┐
                    │        RagQueryService                     │
                    │  ┌──────────────────────────────────────┐  │
                    │  │ 1. Embed User Question              │  │
                    │  │ 2. Vector Search Top-K Results      │  │
                    │  │ 3. Format Context                   │  │
                    │  │ 4. LLM-Generated Answer             │  │
                    │  └──────────────────────────────────────┘  │
                    └────────────────────────────────────────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
                            │   User Response  │
                            │   (Cited Answer) │
                            └──────────────────┘
```

---

## Components

### 1. **RagIngestionService.cs**
**Purpose:** Handles document ingestion pipeline from Azure DevOps to Azure AI Search.

#### Key Features:
- **Repository Traversal**: Recursively fetches markdown files from Azure DevOps repositories
- **Content Processing**: Converts markdown to plain text and removes formatting
- **Smart Chunking**: Splits text into overlapping chunks for better semantic context
- **Batch Embedding**: Efficiently generates vector embeddings in batches
- **Index Management**: Creates and maintains Azure AI Search indexes

#### Class Definition:
```csharp
public sealed class RagIngestionService
```

#### Key Methods:

| Method | Purpose | Parameters |
|--------|---------|----------|
| `EnsureIndexAsync()` | Creates or updates the search index | `ct: CancellationToken` |
| `IngestPathAsync()` | Ingests all markdown files from a path | `path, branch, ct` |

#### Constants:
- **IndexName**: `"ado-wiki"` - Name of the Azure AI Search index
- **ChunkSize**: `1200` - Characters per chunk
- **ChunkOverlap**: `200` - Overlapping characters between chunks

#### Private Methods:
- **`Chunk()`**: Splits text into overlapping segments
- **`SanitizeId()`**: Ensures IDs are valid for Azure Search (alphanumeric + underscore/dash)

---

### 2. **RagModels.cs**
**Purpose:** Defines data models for document chunks in the search index.

#### DocChunk Class:
```csharp
public sealed class DocChunk
```

| Property | Type | Description | Attributes |
|----------|------|-------------|----------|
| `Id` | `string` | Unique identifier (key field) | `[SimpleField(IsKey = true)]` |
| `FilePath` | `string` | Source file path in the repository | `[SearchableField(IsFilterable = true)]` |
| `Content` | `string` | Actual chunk text content | `[SearchableField]` |
| `Embedding` | `IReadOnlyList<float>` | 1536-dim vector embedding | `[VectorSearchField]` |

---

### 3. **RagQueryService.cs**
**Purpose:** Provides semantic search and question-answering capabilities.

#### Key Features:
- **Vector Search**: Finds semantically similar chunks using embeddings
- **Context Assembly**: Aggregates relevant documents
- **LLM Integration**: Uses Semantic Kernel for intelligent response generation
- **Citation Support**: References source files in answers

#### Class Definition:
```csharp
public sealed class RagQueryService
```

#### Primary Method:

| Method | Purpose | Parameters | Returns |
|--------|---------|-----------|----------|
| `AskAsync()` | Search and answer user question | `question, k=5, ct` | `Task<string>` |

**Parameters:**
- `question`: User's natural language question
- `k`: Number of top results to retrieve (default: 5)
- `ct`: Cancellation token for async operations

---

## Features

### 🔹 Feature 1: Document Ingestion
**What it does:**
- Connects to Azure DevOps repositories
- Identifies all markdown files in a specified path
- Downloads and processes content

**Key Capabilities:**
- ✅ Recursive repository traversal
- ✅ Error handling for unavailable files
- ✅ Progress logging
- ✅ Branch-specific ingestion

### 🔹 Feature 2: Intelligent Chunking
**What it does:**
- Breaks large documents into manageable chunks
- Maintains overlap between chunks for context preservation

**Algorithm:**
```
Step = max(1, ChunkSize - ChunkOverlap)
For each position i in text (step by Step):
  Extract chunk of ChunkSize from position i
  If chunk reaches end, stop
```

**Benefits:**
- ✅ Prevents losing context at chunk boundaries
- ✅ Improves semantic search accuracy
- ✅ Optimizes embedding generation costs

### 🔹 Feature 3: Vector Embedding Generation
**What it does:**
- Converts text chunks into 1536-dimensional vectors
- Uses Azure OpenAI Embeddings API
- Batches requests for efficiency (up to 64 per call)

**Technical Details:**
- Embedding Model: Text-embedding-3-small (OpenAI)
- Batch Size: 64 chunks per API call
- Upload Size: 50 chunks per batch to Azure Search

### 🔹 Feature 4: Azure AI Search Integration
**What it does:**
- Stores indexed documents with vectors
- Enables fast similarity search
- Provides filtered queries

**Index Configuration:**
- Algorithm: HNSW (Hierarchical Navigable Small World)
- Vector Profile: "vec-profile"
- Search Strategy: Hybrid (vector + keyword)

### 🔹 Feature 5: Semantic Question-Answering
**What it does:**
- Accepts natural language questions
- Performs vector similarity search
- Retrieves relevant context
- Generates intelligent answers using LLM

**Process Flow:**
1. Embed user question
2. Search for K nearest neighbors
3. Format retrieved chunks as context
4. Send to LLM with prompt template
5. Return cited answer

---

## Data Flow

### Ingestion Pipeline:
```
Input: path = "/docs", branch = "main"
  ↓
Fetch repository tree from Azure DevOps
  ↓
Filter for .md files (blob objects)
  ↓
For each markdown file:
  ├─ Download content
  ├─ Convert to plain text (remove MD formatting)
  ├─ Split into chunks (1200 chars, 200 overlap)
  ├─ Batch chunks (64 per call)
  ├─ Generate embeddings (Azure OpenAI)
  ├─ Create DocChunk objects with:
  │  ├─ Sanitized ID
  │  ├─ File path reference
  │  ├─ Content chunk
  │  └─ Vector embedding
  └─ Upload batches to Azure Search (50 chunks/batch)
  ↓
Output: Number of chunks ingested
```

### Query Pipeline:
```
Input: question = "How do I configure the service?"
  ↓
Embed question (1536-dim vector)
  ↓
Vector search in Azure Search (top-5 results)
  ↓
Retrieve DocChunks with highest similarity scores
  ↓
Format context: [FilePath]\nContent for each result
  ↓
Create prompt with:
  ├─ System role: "Documentation assistant"
  ├─ Context: Retrieved chunks
  └─ Question: User query
  ↓
Invoke LLM (Semantic Kernel)
  ↓
Output: AI-generated answer with citations
```

---

## Configuration & Setup

### Prerequisites:
```
- Azure DevOps account with repository access
- Azure OpenAI API credentials
- Azure AI Search resource
- .NET 6.0+ runtime
```

### Required Services:
1. **AzureDevopsClient**: Configured with PAT (Personal Access Token)
2. **ITextEmbeddingGenerationService**: Connected to Azure OpenAI
3. **SearchIndexClient**: Connected to Azure Cognitive Search
4. **SearchClient**: Specific search client for documents
5. **Kernel**: Microsoft Semantic Kernel instance

### Configuration Example:
```csharp
var services = new ServiceCollection();

// Add Azure DevOps client
services.AddSingleton<AzureDevopsClient>();

// Add OpenAI embeddings
services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
    new AzureOpenAITextEmbeddingGenerationService(
        model: "text-embedding-3-small",
        endpoint: new Uri(aoaiEndpoint),
        apiKey: aoaiKey));

// Add Azure Search
services.AddSingleton(new SearchIndexClient(
    new Uri(searchEndpoint),
    new AzureKeyCredential(searchKey)));

services.AddSingleton(new SearchClient(
    new Uri(searchEndpoint),
    "ado-wiki",
    new AzureKeyCredential(searchKey)));

// Register RAG services
services.AddSingleton<RagIngestionService>();
services.AddSingleton<RagQueryService>();

var provider = services.BuildServiceProvider();
```

---

## API Reference

### RagIngestionService

#### EnsureIndexAsync
```csharp
public async Task EnsureIndexAsync(CancellationToken ct = default)
```
**Creates or updates the Azure AI Search index with vector search capabilities.**

**Usage:**
```csharp
var service = provider.GetRequiredService<RagIngestionService>();
await service.EnsureIndexAsync();
```

#### IngestPathAsync
```csharp
public async Task<int> IngestPathAsync(string path, string branch, CancellationToken ct = default)
```
**Ingests all markdown files from a specific path and branch.**

| Parameter | Type | Description |
|-----------|------|----------|
| `path` | `string` | Repository path (e.g., "/docs") |
| `branch` | `string` | Git branch name (e.g., "main") |
| `ct` | `CancellationToken` | Cancellation token (optional) |

**Returns:** Number of chunks successfully ingested

**Usage:**
```csharp
int chunksIngested = await service.IngestPathAsync("/docs", "main");
Console.WriteLine($"Ingested {chunksIngested} chunks");
```

---

### RagQueryService

#### AskAsync
```csharp
public async Task<string> AskAsync(string question, int k = 5, CancellationToken ct = default)
```
**Searches the indexed documents and generates an answer using the LLM.**

| Parameter | Type | Description | Default |
|-----------|------|-------------|----------|
| `question` | `string` | User's question | Required |
| `k` | `int` | Number of top results to retrieve | 5 |
| `ct` | `CancellationToken` | Cancellation token | default |

**Returns:** AI-generated answer with source citations

**Usage:**
```csharp
var queryService = provider.GetRequiredService<RagQueryService>();
string answer = await queryService.AskAsync(
    "How do I set up authentication?", 
    k: 5
);
Console.WriteLine(answer);
```

**Example Output:**
```
Based on the documentation, to set up authentication:

1. Configure API credentials in appsettings.json
2. Use the AuthenticationMiddleware in your startup
3. Enable OAuth2 for external integrations

[/docs/setup/authentication.md]
[/docs/api/security.md]
```

---

## Usage Examples

### Example 1: Full Ingestion Workflow
```csharp
// Get services
var ingestionService = provider.GetRequiredService<RagIngestionService>();

// Ensure index exists
await ingestionService.EnsureIndexAsync();

// Ingest documentation from main branch
int chunksAdded = await ingestionService.IngestPathAsync(
    path: "/wiki/docs",
    branch: "main"
);

Console.WriteLine($"Successfully ingested {chunksAdded} documentation chunks");
```

### Example 2: Question-Answering
```csharp
var queryService = provider.GetRequiredService<RagQueryService>();

// Ask a question about the indexed documentation
string answer = await queryService.AskAsync(
    question: "What are the system requirements?",
    k: 3  // Get top-3 relevant chunks
);

Console.WriteLine($"Answer: {answer}");
```

### Example 3: Multiple Path Ingestion
```csharp
var ingestionService = provider.GetRequiredService<RagIngestionService>();
await ingestionService.EnsureIndexAsync();

// Ingest multiple documentation paths
string[] paths = { "/wiki/docs", "/wiki/guides", "/wiki/api" };

foreach (var path in paths)
{
    try
    {
        int chunks = await ingestionService.IngestPathAsync(path, "main");
        Console.WriteLine($"Ingested {chunks} chunks from {path}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to ingest {path}: {ex.Message}");
    }
}
```

### Example 4: Batch Question Processing
```csharp
var queryService = provider.GetRequiredService<RagQueryService>();

var questions = new[]
{
    "How do I configure the database?",
    "What are the available APIs?",
    "How do I enable logging?"
};

foreach (var question in questions)
{
    var answer = await queryService.AskAsync(question);
    Console.WriteLine($"Q: {question}");
    Console.WriteLine($"A: {answer}\n");
}
```

---

## Performance Considerations

### Chunking Strategy
- **ChunkSize: 1200 characters** - Balances context with embedding efficiency
- **ChunkOverlap: 200 characters** - Prevents critical information loss at boundaries

### Batching
- **Embedding Batch**: 64 chunks per Azure OpenAI API call
- **Upload Batch**: 50 chunks per Azure Search upload
- **Benefit**: Reduces API calls and improves throughput

### Search Configuration
- **Vector Search Algorithm**: HNSW (fast approximate nearest neighbor)
- **Default K**: 5 results (configurable)
- **Vector Dimensions**: 1536 (OpenAI standard)

---

## Error Handling

The service includes robust error handling:

1. **File Processing Errors**: Skips unavailable files with warning logs
2. **API Failures**: Logs and continues with remaining items
3. **Cancellation Support**: Respects CancellationToken for graceful shutdown

---

## Next Steps

1. Deploy the service to your environment
2. Configure Azure DevOps repository connection
3. Run initial ingestion on your documentation paths
4. Test with sample questions
5. Monitor logs and refine chunking parameters if needed
