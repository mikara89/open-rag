# Local/OpenAI-compatible Chat

OpenRAG supports OpenAI-compatible chat completion APIs (DeepSeek, LM Studio, Ollama, etc.).

## Configuration

```json
{
  "AI": {
    "Chat": {
      "Provider": "Mock",
      "BaseUrl": "http://localhost:1234/v1",
      "ApiKey": "lm-studio",
      "Model": "mock-chat",
      "TimeoutSeconds": 120,
      "Temperature": 0.2,
      "MaxTokens": 1024
    }
  }
}
```

| Key | Description |
|-----|-------------|
| `Provider` | `"Mock"` or `"OpenAICompatible"` |
| `BaseUrl` | API base URL (without `/chat/completions`) |
| `ApiKey` | Bearer token for Authorization header |
| `Model` | Chat model name |
| `Temperature` | Response randomness (0.0–1.0) |
| `MaxTokens` | Max output tokens (null = unlimited) |

## DeepSeek

Store the API key in user secrets:

```powershell
dotnet user-secrets set "AI:Chat:ApiKey" "sk-your-key" --project src/OpenRAG.Api
```

Then configure:

```json
{
  "AI": {
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "https://api.deepseek.com/v1",
      "Model": "deepseek-chat"
    }
  }
}
```

## LM Studio

1. Start LM Studio server on port 1234
2. Load a chat model
3. Verify:

```bash
curl.exe "http://localhost:1234/v1/models"
```

4. Test chat (Git Bash):

```bash
curl.exe -X POST "http://localhost:1234/v1/chat/completions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer lm-studio" \
  -d '{"model":"<model-id>","messages":[{"role":"user","content":"Say hello"}]}'
```

5. Configure:

```json
{
  "AI": {
    "Chat": {
      "Provider": "OpenAICompatible",
      "BaseUrl": "http://localhost:1234/v1",
      "ApiKey": "lm-studio",
      "Model": "<model-id>"
    }
  }
}
```

## Switching back to Mock

```json
{ "AI": { "Chat": { "Provider": "Mock" } } }
```

## Shell syntax

| Shell | Line continuation |
|-------|------------------|
| Git Bash / WSL | `\` |
| Windows CMD | `^` |
| PowerShell | `` ` `` (backtick) |

## Troubleshooting

- If RAG returns mock answers, check `AI:Chat:Provider` is set to `"OpenAICompatible"`.
- API keys are stored in user secrets, not committed to git.
- DeepSeek does NOT support embeddings — use Mock or LM Studio for embeddings.
