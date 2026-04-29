# OrigamAI

Self-hosted RAG over the [Origam Community](https://community.origam.com) forum, built on **Microsoft Kernel Memory** with **Pollinations.AI** as the OpenAI-compatible LLM/embedding backend.

## Quick start

```bash
cd OrigamAI
dotnet run
```

Then open the **interactive API UI**:

> 👉 http://localhost:5244/scalar/v1

The root `/` redirects there. Raw OpenAPI JSON: http://localhost:5244/openapi/v1.json.

If you launch the `https` profile use `https://localhost:7035/scalar/v1` instead.

## API

All endpoints are under `/api/rag`.

| Method | Path | Description |
|---|---|---|
| POST | `/api/rag/ingest-text` | Ingest a raw text chunk with optional tags |
| POST | `/api/rag/ingest-origam?categoryId=&maxTopicsPerCategory=&maxPages=` | Pull topics from the Origam Discourse forum and index them |
| POST | `/api/rag/ask` | RAG question; optional `filter` like `source=origam-community` |
| GET  | `/api/rag/search?q=&limit=` | Plain vector search (no LLM) |
| DELETE | `/api/rag/document/{id}` | Remove a document from the index |

Ready-to-fire requests are in [`OrigamAI/OrigamAI.http`](OrigamAI/OrigamAI.http).

## Configuration

`OrigamAI/appsettings.json`:

```json
{
  "Pollinations": {
    "ApiKey": "sk_...",
    "Endpoint": "https://text.pollinations.ai/openai",
    "TextModel": "openai",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "OrigamCommunity": {
    "BaseUrl": "https://community.origam.com"
  }
}
```

## Storage

Vectors and ingested files live on disk under `OrigamAI/_kmstore/`:

- `_kmstore/vectors/` — one JSON per chunk (embedding + text + tags)
- `_kmstore/files/` — original documents and pipeline artifacts

To relocate, edit the `Directory = ...` lines in [`OrigamAI/Program.cs`](OrigamAI/Program.cs).

When the SimpleVectorDb gets too slow (tens of thousands of chunks), swap it for Qdrant or Elasticsearch — only the builder line changes.

## Notes

- Embeddings go through Pollinations' `/v1/embeddings`. If that endpoint is unavailable, install Ollama (`ollama pull nomic-embed-text`) and switch to `WithOllamaTextEmbeddingGeneration(...)` in `Program.cs`.
- Scalar UI is enabled only in `Development`.
- `app.UseHttpsRedirection()` is on — `http://...` requests bounce to HTTPS.


To install Olama for emmbedings 

irm https://ollama.com/install.ps1 | iex
ollama pull nomic-embed-text
ollama pull bge-m3
ollama list
curl http://localhost:11434/api/tags
