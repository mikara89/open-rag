# Local Embeddings with LM Studio

This guide explains how to use LM Studio as a local OpenAI-compatible embedding
provider for the OpenRAG pipeline.

## Prerequisites

1. Download and install [LM Studio](https://lmstudio.ai/).
2. Start LM Studio and load an embedding model such as:
   - `nomic-embed-text-v1.5`
   - `text-embedding-nomic-embed-text-v1.5`
3. Start the local server on port `1234` via the LM Studio UI.

## Verify LM Studio is running

```bash
# List available models
curl http://localhost:1234/v1/models
```

```bash
# Test embeddings endpoint (Git Bash / WSL / PowerShell)
curl.exe -X POST "http://localhost:1234/v1/embeddings" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer lm-studio" \
  -d '{"model":"nomic-embed-text-v1.5","input":"This is a test document chunk."}'
```

Expected response shape:

```json
{
  "object": "list",
  "data": [{
    "object": "embedding",
    "index": 0,
    "embedding": [0.1, 0.2, 0.3, ...]
  }],
  "model": "nomic-embed-text-v1.5",
  "usage": {
    "prompt_tokens": 5,
    "total_tokens": 5
  }
}
```

## Configure OpenRAG to use LM Studio

Edit `appsettings.Development.json` (both in `src/OpenRAG.Api/` and `src/OpenRAG.Worker/`):

```json
{
  "AI": {
    "Embeddings": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "ApiKey": "lm-studio",
      "Model": "nomic-embed-text-v1.5",
      "EmbeddingVersion": "v1",
      "TimeoutSeconds": 120
    }
  }
}
```

Key settings:

| Key | Description |
|-----|-------------|
| `Provider` | Set to `"OpenAICompatible"` to use LM Studio; `"Mock"` for deterministic mock vectors |
| `BaseUrl` | LM Studio's local API base URL |
| `ApiKey` | LM Studio API key (default: `lm-studio`) |
| `Model` | Embedding model name as loaded in LM Studio |
| `EmbeddingVersion` | Version tag stored with embeddings |
| `TimeoutSeconds` | HTTP request timeout |

## Switching back to Mock

To go back to mock embeddings (e.g., for tests or when LM Studio is not running):

```json
{
  "AI": {
    "Embeddings": {
      "Provider": "Mock"
    }
  }
}
```

## Notes

- The default provider is `"Mock"` — no external service required for local development.
- The pipeline uses the same model for all embeddings in a single processing run.
- Embedding dimensions are determined dynamically from the API response — no hardcoded values.
- The `Authorization` header uses `Bearer {ApiKey}` format.
- On Git Bash, use `\` for line continuation. Windows CMD uses `^`.

## Troubleshooting

### RAG returns "no results" after switching embedding providers

If `/api/rag/ask` returns no results (or a diagnostic message about mismatched embeddings):

1. **Ensure the document was uploaded after switching the `AI:Embeddings:Provider`.** Existing embeddings stored in the database use the provider/model/dimensions from when they were created. Switching the provider does not re-index existing documents.

2. **Ensure ingestion and ask use the same embedding provider/model.** The pipeline (Worker) and the RAG endpoint (API) must use the same `AI:Embeddings` configuration. If they differ, the question embedding will not match the stored document embeddings.

3. **Mock embeddings are 8-dimensional.** `nomic-embed-text` is typically 768-dimensional (or 1376 for v1.5). These are incompatible — cosine similarity requires matching dimensions.

4. **Re-upload/re-index documents after changing the embedding model.** This ensures new embeddings are generated with the current provider/model.

5. **Check the diagnostic message.** The API now returns a specific message when embeddings exist but none are compatible:
   ```
   Indexed embeddings exist (10 total), but none match the current query embedding:
   model=nomic-embed-text, dimensions=768.
   ```

### Quick fix: Switch back to Mock

```json
{ "AI": { "Embeddings": { "Provider": "Mock" } } }
```

Then re-upload your document — both pipeline and RAG will use matching 8-dim mock vectors.
