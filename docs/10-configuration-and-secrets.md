# 10 — Configuration and Secrets

## Overview

OpenRAG uses a layered configuration system with explicit options classes, startup validation, and secure secret resolution. Every active provider has typed options that are validated at startup.

## Configuration layers

```
appsettings.json                  → Production defaults
appsettings.Development.json      → Dev overrides (local connections)
User Secrets                      → Local secrets (dotnet user-secrets)
Environment variables             → Overrides from Aspire or deployment
Command-line arguments            → CLI overrides
```

Aspire injects connection strings and service URLs as environment variables automatically.

## Runtime infrastructure requirements

- PostgreSQL must have the pgvector extension. The Aspire AppHost uses `pgvector/pgvector:pg17`.
- API and Worker use PostgreSQL for application/CAP state and RabbitMQ for cross-process CAP transport.
- The MVP file provider writes to the local filesystem; an S3-compatible provider is not implemented yet.
- Docling Serve and external AI endpoints are needed only when their non-mock providers are selected.

Build, unit, architecture, integration-model, format, and documentation checks do not require live PostgreSQL, RabbitMQ, Docling, or AI providers. GitHub CI does not start Aspire services or read developer-machine configuration.

JWT Bearer authentication is configured separately under `Authentication:Jwt` and is required for API startup. Authority and Audience belong in environment variables or API user secrets, never committed settings. See [JWT authentication](15-authentication.md).

## Provider matrix

| Category       | Provider           | Description                                | Requires external service |
|----------------|--------------------|--------------------------------------------|--------------------------|
| Preprocessing  | Mock               | Built-in mock, no external dependencies     | No                       |
| Preprocessing  | DoclingServe       | Docling Serve REST API for document parsing | Yes                      |
| Chunking       | DoclingJson        | Docling JSON-aware chunker with fallback    | No                       |
| Chunking       | SimpleMarkdown     | Plain markdown chunker                      | No                       |
| Embeddings     | Mock               | Deterministic 8-dimension vectors           | No                       |
| Embeddings     | OpenAICompatible   | Any OpenAI-compatible /v1/embeddings API    | Yes                      |
| Chat           | Mock               | Deterministic mock responses                | No                       |
| Chat           | OpenAICompatible   | Any OpenAI-compatible chat completions API  | Yes                      |
| Storage        | Local              | Local filesystem storage                    | No                       |

## Required configuration per provider

### Preprocessing

**Mock:** No configuration needed.

**DoclingServe:**
```json
{
  "Preprocessing": {
    "Docling": {
      "Provider": "DoclingServe",
      "BaseUrl": "http://localhost:5001",
      "ConvertFilePath": "/v1/convert/file",
      "TimeoutSeconds": 300,
      "EnableOcr": false,
      "ToFormats": ["md", "json"]
    }
  }
}
```

- `BaseUrl` — **required**. URL of the Docling Serve instance.
- `ConvertFilePath` — **required**. API path for file conversion.
- `TimeoutSeconds` — optional, defaults to 300.
- `EnableOcr` — optional, defaults to false.
- `ToFormats` — optional, defaults to `["md", "json"]`.

### Chunking

```json
{
  "Chunking": {
    "Provider": "DoclingJson",
    "MaxChunkCharacters": 2000,
    "OverlapCharacters": 200,
    "UseDoclingJsonWhenAvailable": true
  }
}
```

No external services needed for either provider.

### Embeddings

**Mock:** No configuration needed.

**OpenAICompatible:**
```json
{
  "AI": {
    "Embeddings": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "Model": "nomic-embed-text",
      "ApiKey": "lm-studio",
      "TimeoutSeconds": 120
    }
  }
}
```

- `BaseUrl` — **required** when Provider is OpenAICompatible.
- `Model` — **required** when Provider is OpenAICompatible.
- `ApiKey` — **required** when Provider is OpenAICompatible (can also use `ApiKeyEnvironmentVariable`).
- `ApiKeyEnvironmentVariable` — optional, name of env var containing the key.
- `Dimensions` — optional, output vector dimension hint.
- `TimeoutSeconds` — optional, defaults to 120.

### Chat

**Mock:** No configuration needed.

**OpenAICompatible:**
```json
{
  "AI": {
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat",
      "ApiKey": "sk-your-key",
      "TimeoutSeconds": 120,
      "Temperature": 0.2,
      "MaxTokens": 1024
    }
  }
}
```

- `BaseUrl` — **required** when Provider is OpenAICompatible.
- `Model` — **required** when Provider is OpenAICompatible.
- `ApiKey` — **required** when Provider is OpenAICompatible (can also use `ApiKeyEnvironmentVariable`).
- `ApiKeyEnvironmentVariable` — optional, name of env var containing the key.
- `TimeoutSeconds` — optional, defaults to 120.
- `Temperature` — optional, defaults to 0.2.
- `MaxTokens` — optional, defaults to 1024.

### Storage

```json
{
  "Storage": {
    "Provider": "Local",
    "LocalRootPath": "../../.openrag-storage"
  }
}
```

- `Provider` — must be "Local".
- `LocalRootPath` — optional, path for file storage. Defaults to `.openrag-storage`.

## API key resolution

API keys are resolved in this priority order:

1. **Explicit config value** — `ApiKey` in appsettings (skip placeholders like `YOUR_DEEPSEEK_API_KEY_HERE`, `<from secrets>`)
2. **Named environment variable** — from `ApiKeyEnvironmentVariable` if configured
3. **Conventional environment variables** — auto-detected:

| Service      | Conventional env vars checked                        |
|--------------|------------------------------------------------------|
| Chat         | `OPENAI_API_KEY`, `DEEPSEEK_API_KEY`, `OPENRAG_CHAT_API_KEY` |
| Embeddings   | `OPENAI_API_KEY`, `OPENRAG_EMBEDDINGS_API_KEY`      |

API keys are **never** logged. Only presence/absence (`"present"` / `"missing"`) is recorded.

GitHub Actions contains no provider credentials and does not require repository secrets. Do not add raw keys to workflow files, application settings, test fixtures, logs, documentation, or pull-request text. A future live smoke-test job must receive credentials through an approved environment or secret store and must keep mock/dependency-free validation separate.

## Example configurations

### Mock-only (zero dependencies)

```json
{
  "Preprocessing": { "Docling": { "Provider": "Mock" } },
  "Chunking": { "Provider": "DoclingJson" },
  "AI": {
    "Embeddings": { "Provider": "Mock" },
    "Chat": { "Provider": "Mock" }
  },
  "Storage": { "Provider": "Local" }
}
```

### DoclingServe + DeepSeek

```json
{
  "Preprocessing": {
    "Docling": {
      "Provider": "DoclingServe",
      "BaseUrl": "http://localhost:5001",
      "ConvertFilePath": "/v1/convert/file"
    }
  },
  "Chunking": { "Provider": "DoclingJson" },
  "AI": {
    "Embeddings": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat",
      "ApiKeyEnvironmentVariable": "DEEPSEEK_API_KEY"
    },
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat",
      "ApiKeyEnvironmentVariable": "DEEPSEEK_API_KEY"
    }
  },
  "Storage": { "Provider": "Local" }
}
```

With this config, set `DEEPSEEK_API_KEY` in your environment.

### Local LM Studio embeddings + local chat

```json
{
  "Preprocessing": { "Docling": { "Provider": "Mock" } },
  "Chunking": { "Provider": "DoclingJson" },
  "AI": {
    "Embeddings": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "Model": "nomic-embed-text-v1.5",
      "ApiKey": "lm-studio"
    },
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "Model": "local-model",
      "ApiKey": "lm-studio"
    }
  },
  "Storage": { "Provider": "Local" }
}
```

## Startup validation

All active providers are validated at startup via `IValidateOptions<T>`. The application fails fast with clear messages when:

- An unknown provider name is configured.
- Required fields for a provider are missing (e.g., `BaseUrl` for DoclingServe).
- API key source is missing for OpenAI-compatible providers.
- JWT Authority or Audience is missing, the Authority is not an absolute HTTP(S) URI or violates the HTTPS-metadata policy, claim names are blank, or clock skew is outside 0–300 seconds.

Example error message:

```
Preprocessing:Docling:BaseUrl is required when Provider is DoclingServe.
Preprocessing:Docling:ConvertFilePath is required when Provider is DoclingServe.
AI:Chat:ApiKey or ApiKeyEnvironmentVariable is required when Provider is OpenAICompatible.
```

Mock providers always pass validation without external configuration.

## Diagnostics endpoint

`GET /api/system/providers` returns the current provider configuration:

```json
{
  "preprocessing": {
    "provider": "DoclingServe",
    "configured": true,
    "baseUrl": "http://localhost:5001",
    "convertFilePath": "/v1/convert/file"
  },
  "chunking": {
    "provider": "DoclingJson",
    "configured": true,
    "maxChunkCharacters": 2000,
    "overlapCharacters": 200
  },
  "embeddings": {
    "provider": "Mock",
    "configured": true,
    "model": "mock",
    "apiKeyPresent": false
  },
  "chat": {
    "provider": "OpenAICompatible",
    "configured": true,
    "baseUrl": "https://api.deepseek.com/v1",
    "model": "deepseek-chat",
    "apiKeyPresent": true
  },
  "storage": {
    "provider": "Local",
    "configured": true,
    "localRootPath": "../../.openrag-storage"
  }
}
```

- API keys are **never** exposed — only `apiKeyPresent: true/false`.
- Validation errors appear in `validationErrors` when `configured: false`.
- Storage paths are exposed (local dev only — safe for MVP).

## Troubleshooting common errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Preprocessing:Docling:BaseUrl is required` | DoclingServe selected but no BaseUrl | Set `BaseUrl` or switch to `Mock` |
| `AI:Chat:ApiKey ... is required` | OpenAICompatible chat but no key | Set `ApiKey`, `ApiKeyEnvironmentVariable`, or env var |
| `Provider 'X' is not recognized` | Unknown provider name | Check spelling, use valid provider name |
| `Connection string 'openrag-db' not found` | No database connection | Ensure Aspire is running or set connection string |
| App starts but returns mock answers | AI providers defaulted to Mock | Configure OpenAICompatible for Chat/Embeddings |
