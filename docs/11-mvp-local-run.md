# 11 — MVP Local Run

## Prerequisites

- **.NET SDK 10.0** or later
- **Podman** or **Docker** (for DoclingServe + infrastructure containers)
- A PostgreSQL image with the **pgvector** extension (the AppHost supplies `pgvector/pgvector:pg17`)
- **DeepSeek** or **OpenAI-compatible** API key (if using real chat/embeddings)
- Optional: **LM Studio** or local OpenAI-compatible server for local embeddings/chat

## Quick start: mock providers

This mode uses mock providers for preprocessing, embeddings, and chat, so it needs no Docling or external AI service. PostgreSQL/pgvector and RabbitMQ still run as local infrastructure containers.

### 0. Configure JWT authentication

The API fails startup when `Authentication:Jwt:Authority` or `Authentication:Jwt:Audience` is absent or invalid. Use the values assigned by your identity provider; do not commit them to application settings.

```powershell
dotnet user-secrets set "Authentication:Jwt:Authority" "https://idp.example.com" --project src/OpenRAG.Api
dotnet user-secrets set "Authentication:Jwt:Audience" "openrag-api" --project src/OpenRAG.Api
```

Equivalent environment variables are `Authentication__Jwt__Authority` and `Authentication__Jwt__Audience`. HTTPS metadata is required by default. Obtain an access token from the configured identity provider and keep it only in the current process:

```powershell
$env:OPENRAG_ACCESS_TOKEN = "<access token obtained outside OpenRAG>"
```

OpenRAG validates tokens but does not issue them. See [JWT authentication](15-authentication.md) for the full contract.
The full MVP smoke test calls the provider-diagnostics endpoint, so its token must include exactly one valid GUID user-ID claim and the configured `admin` role.

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
./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN -Model "mock-chat"
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
./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
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
dotnet build OpenRAG.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test OpenRAG.slnx --configuration Release --no-build \
  --logger trx \
  --results-directory artifacts/test-results \
  --collect "XPlat Code Coverage"
dotnet format whitespace OpenRAG.slnx --verify-no-changes --no-restore
dotnet format style OpenRAG.slnx --verify-no-changes --no-restore

# Same checks as GitHub CI, including documentation
./scripts/ci-local.ps1

# Restore, Release build/test, TRX, coverage, and format checks
./scripts/verify.ps1

# Documentation only
./scripts/docs-check.ps1

# API smoke test (requires running services)
./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN
./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
```

GitHub Actions runs restore, Release build, tests with TRX and Cobertura coverage output, both format checks, and documentation validation for pull requests and pushes to `main`. `setup-dotnet` keys its NuGet cache from central package/build files, tool manifests, and project files because Aspire injects OS-specific packages that make one cross-platform lock file impractical. Test results and coverage are uploaded as separate artifacts. CI does not start Aspire or run the live smoke test, so it does not depend on PostgreSQL, RabbitMQ, Docling, external AI providers, local state, or secrets.

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

### Authentication configuration failure

**Symptom:** The API fails startup with an `Authentication:Jwt` validation message.

**Fix:** Set an absolute HTTPS Authority and a non-empty Audience through user secrets or environment variables. Do not disable issuer, audience, signature, or lifetime validation. `RequireHttpsMetadata=false` is limited to isolated local development with an HTTP metadata endpoint and must not be used in production.

### API returns 401 or 403

- `401` with `WWW-Authenticate: Bearer` means the token is missing, malformed, expired, unsigned, or failed issuer, audience, or signature validation.
- `403` means the token was authenticated but lacks exactly one usable GUID user-ID claim or the required role.
- Provider diagnostics requires the configured administrator role; ordinary authenticated users receive `403`.

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

### Pgvector extension missing from PostgreSQL

**Symptom:** `dotnet ef database update` fails with `type "vector" does not exist`.

**Fix:** The pgvector extension must be enabled. The migration idempotently runs
`CREATE EXTENSION IF NOT EXISTS vector;`. If this fails, your PostgreSQL image
may not include pgvector.

**Solutions:**
1. **Use pgvector image (recommended):** The Aspire AppHost uses
   `pgvector/pgvector:pg17` which includes pgvector. Restart containers.
2. **Manual enable:** Connect to PostgreSQL and run:
   ```sql
   CREATE EXTENSION IF NOT EXISTS vector;
   ```
3. **Reset local DB:** If the extension is installed but the vector type doesn't work,
   drop and recreate:
   ```bash
   docker compose down -v  # or podman volume rm
   dotnet run --project src/OpenRAG.AppHost  # Recreates with pgvector support
   ```

### How pgvector changes retrieval

With pgvector-backed storage:

- **Server-side search:** Cosine distance (`<=>`) is computed in PostgreSQL,
  not in application memory. This enables production-scale vector search.
- **Vector column type:** Embeddings are stored in a native `vector` column
  (was `bytea` with float serialization). The migration
  `MigrateEmbeddingVectorToPgvector` handles the type change.
- **Search behavior unchanged:** The `IVectorSearchService` abstraction is
  unchanged. Mock providers and tests still work without pgvector.
- **Existing data:** Back up the database before applying the migration. Existing
  serialized `bytea` embedding values may not be safely convertible to native vectors;
  treat them as derived data that may need to be dropped during migration and regenerated.
- **Regeneration:** After migration, call
  `POST /api/documents/{id}/reprocess` with `forceEmbeddings: true` for each affected
  document. This recreates embeddings through the supported processing pipeline.

## MVP Acceptance Checklist

MVP is accepted when all of the following pass:

- [ ] **Build:** `dotnet build OpenRAG.slnx` — 0 errors
- [ ] **Tests:** `dotnet test OpenRAG.slnx` — all tests pass (364 in the P0.2 validation run)
- [ ] **Format:** `dotnet format whitespace|style --verify-no-changes` — clean
- [ ] **Authentication:** Missing and invalid tokens return 401; a valid token with a GUID user-ID claim is accepted
- [ ] **Provider diagnostics:** `GET /api/system/providers` returns configured providers only for an administrator token
- [ ] **Upload:** `POST /api/documents/upload` returns 201 with documentId
- [ ] **Processing:** Document reaches `Ready` status within timeout
- [ ] **Processing history:** Status response includes `processingRuns` with completed steps (Preprocess, Chunk, Intelligence, GenerateEmbeddings)
- [ ] **Intelligence:** `GET .../versions/{versionId}/intelligence` returns classification, summary, keywords
- [ ] **Detail intelligence:** `GET /api/documents/{id}` includes `intelligence` block with classification/summary
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
- [ ] **Smoke test:** `./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN` passes
