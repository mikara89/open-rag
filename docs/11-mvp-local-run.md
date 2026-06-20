# 11 — MVP Local Run

## Prerequisites

- **.NET SDK 10.0** or later
- **Podman** or **Docker** (for DoclingServe + infrastructure containers)
- **DeepSeek** or **OpenAI-compatible** API key (if using real chat/embeddings)
- Optional: **LM Studio** or local OpenAI-compatible server for local embeddings/chat

## Quick start: mock-only (zero external dependencies)

This mode uses Mock providers for preprocessing, embeddings, and chat. Only infrastructure (PostgreSQL + RabbitMQ) needs containers.

### 1. Configure mock providers

`src/OpenRAG.Api/appsettings.Development.json` (default):

```json
{
  "Preprocessing": { "Docling": { "Provider": "Mock" } },
  "Chunking": { "Provider": "DoclingJson" },
  "AI": {
    "Embeddings": { "Provider": "Mock" },
    "Chat": { "Provider": "Mock" }
  }
}
```

### 2. Start Aspire AppHost

```bash
dotnet run --project src/OpenRAG.AppHost
```

This starts:
- PostgreSQL (pgvector/pg17)
- RabbitMQ (with management plugin)
- OpenRAG API
- OpenRAG Worker

Wait for all resources to be healthy in the Aspire dashboard.

### 3. Run MVP smoke test

```bash
./scripts/mvp-smoke-test.ps1 -Model "mock-chat"
```

## Quick start: DoclingServe + DeepSeek

### 1. Set API key

Choose one:

```bash
# Option A: Environment variable
$env:DEEPSEEK_API_KEY = "sk-your-key"

# Option B: User secrets
dotnet user-secrets set "AI:Chat:ApiKey" "sk-your-key" --project src/OpenRAG.Api
dotnet user-secrets set "AI:Chat:ApiKey" "sk-your-key" --project src/OpenRAG.Worker
```

### 2. Configure providers

`src/OpenRAG.Api/appsettings.Development.json`:

```json
{
  "Preprocessing": {
    "Docling": {
      "Provider": "DoclingServe",
      "BaseUrl": "http://localhost:5001",
      "ConvertFilePath": "/v1/convert/file"
    }
  },
  "AI": {
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat",
      "ApiKeyEnvironmentVariable": "DEEPSEEK_API_KEY"
    },
    "Embeddings": {
      "Provider": "Mock"
    }
  }
}
```

### 3. Start Aspire AppHost (includes DoclingServe container)

```bash
dotnet run --project src/OpenRAG.AppHost
```

### 4. Run MVP smoke test

```bash
./scripts/mvp-smoke-test.ps1 -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
```

## Quick start: DoclingServe + local LM Studio embeddings + DeepSeek chat

### 1. Start LM Studio

Start a local server on `http://localhost:1234` with an embedding model loaded (e.g., `nomic-embed-text-v1.5`).

### 2. Configure

```json
{
  "AI": {
    "Embeddings": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "Model": "nomic-embed-text-v1.5",
      "ApiKey": "lm-studio"
    },
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat",
      "ApiKeyEnvironmentVariable": "DEEPSEEK_API_KEY"
    }
  }
}
```

## Validation commands

```bash
# Full static validation
dotnet restore OpenRAG.slnx
dotnet build OpenRAG.slnx
dotnet test OpenRAG.slnx
dotnet format whitespace OpenRAG.slnx --verify-no-changes --no-restore
dotnet format style OpenRAG.slnx --verify-no-changes --no-restore

# Or use the script
./scripts/verify.ps1

# API smoke test (requires running services)
./scripts/mvp-smoke-test.ps1
./scripts/mvp-smoke-test.ps1 -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
```

## Troubleshooting

### RabbitMQ/CAP connectivity

**Symptom:** Document stuck in `Uploaded` state, no processing starts.

**Check:**
- RabbitMQ is running: `docker ps | grep rabbitmq` or Aspire dashboard
- Worker is running and subscribed to CAP topics
- Check Worker logs for `Received document.uploaded`

### Docling Serve unavailable

**Symptom:** Preprocess step failed with `Failed to call Docling Serve`.

**Check:**
- DoclingServe container is running (Aspire dashboard)
- BaseUrl is correct: `http://localhost:5001`
- Try: `curl http://localhost:5001/v1/convert/file`

### Docling 422/400 request contract issue

**Symptom:** Docling returns HTTP 422 or 400.

**Fix:** Check that the uploaded file format is supported by Docling (PDF, DOCX, PPTX, images, HTML, Markdown).

### API key missing

**Symptom:** Startup validation error: `AI:Chat:ApiKey or ApiKeyEnvironmentVariable is required when Provider is OpenAICompatible.`

**Fix:** Set the API key via environment variable or user secrets. See the configuration section above.

### Provider validation failure

**Symptom:** App fails to start with `Preprocessing:Docling:Provider 'X' is not recognized.`

**Fix:** Use a valid provider name: `Mock` or `DoclingServe`. Check `GET /api/system/providers` for current configuration.

### Document stuck in Processing

**Symptom:** Status stays `Processing` and never reaches `Ready`.

**Check:**
1. `GET /api/documents/{id}/status` — look at `processingRuns` for failed steps
2. Check Worker logs for correlation ID
3. Verify RabbitMQ is running
4. Reprocess: `POST /api/documents/{id}/reprocess` with `forcePreprocess: true`

### Failed run visible in status history

**Symptom:** Processing history shows a Failed step.

**Fix:** Check `errorMessage` on the failed step for details. Common causes:
- Docling Serve unavailable → restart Docling, reprocess
- Embedding provider unavailable → check LM Studio/Ollama, reprocess
- Invalid file → upload a supported format

## MVP Acceptance Checklist

MVP is accepted when all of the following pass:

- [ ] **Build:** `dotnet build OpenRAG.slnx` — 0 errors
- [ ] **Tests:** `dotnet test OpenRAG.slnx` — all 324+ tests pass
- [ ] **Format:** `dotnet format whitespace|style --verify-no-changes` — clean
- [ ] **Provider diagnostics:** `GET /api/system/providers` returns configured providers
- [ ] **Upload:** `POST /api/documents/upload` returns 201 with documentId
- [ ] **Processing:** Document reaches `Ready` status within timeout
- [ ] **Processing history:** Status response includes `processingRuns` with completed steps
- [ ] **List:** Document appears in `GET /api/documents` results
- [ ] **Detail:** `GET /api/documents/{id}` shows latest version, chunk count, embedding metadata
- [ ] **Markdown artifact:** `GET .../artifacts/markdown` returns content
- [ ] **JSON artifact:** `GET .../artifacts/json` returns content or a reasonable empty/404
- [ ] **Chunks list:** `GET .../chunks` returns paginated chunks
- [ ] **First chunk detail:** `GET .../chunks/{chunkId}` shows content and embedding metadata
- [ ] **RAG ask:** `POST /api/rag/ask` returns an answer with citations
- [ ] **Reprocess:** `POST /api/documents/{id}/reprocess` returns 202 and document reaches Ready again
- [ ] **Second RAG ask:** Returns answer after reprocess
- [ ] **Delete:** `DELETE /api/documents/{id}` returns 204
- [ ] **Delete verify:** Detail and status return 404 after delete
- [ ] **Configuration validation:** Unknown provider names fail with clear error
- [ ] **Secrets:** API keys are never exposed in logs or diagnostics endpoint
- [ ] **Smoke test:** `./scripts/mvp-smoke-test.ps1` passes
