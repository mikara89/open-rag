# Local Docling Serve

Real document preprocessing using Docling Serve REST API.

## Start with Podman/Docker

```bash
podman run --rm -p 5001:5001 -e DOCLING_SERVE_ENABLE_UI=1 quay.io/docling-project/docling-serve
```

First run may be slow as Docling downloads models.

## Verify

```bash
# OpenAPI docs
curl http://localhost:5001/docs

# Web UI
# Open http://localhost:5001/ui in browser

# Test conversion
curl -X POST http://localhost:5001/v1/convert/file \
  -F "file=@README.md"
```

## Configure OpenRAG

```json
{
  "Preprocessing": {
    "Docling": {
      "Provider": "DoclingServe",
      "BaseUrl": "http://localhost:5001",
      "ConvertFilePath": "/v1/convert/file",
      "TimeoutSeconds": 300,
      "IncludeMarkdown": true,
      "IncludeJson": true,
      "EnableOcr": false
    }
  }
}
```

## Smoke test

1. Start Docling Serve
2. Start OpenRAG AppHost
3. Upload a PDF/DOCX/Markdown file
4. Poll status until Ready
5. Call `/api/rag/ask`
6. Retrieved chunks should contain **real document text**, not "Mock preprocessed content"

```powershell
./scripts/smoke-test-rag.ps1 -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
```

## Troubleshooting

### Endpoint path changed
If the endpoint fails, check Docling Serve OpenAPI docs at `http://localhost:5001/docs` and update `ConvertFilePath`.

### Slow first conversion
Docling downloads models on first use — subsequent conversions are faster.

### AppHost doesn't start
Set `Preprocessing:Docling:Provider` to `"Mock"` to run without Docling.
The AppHost includes a Docling Serve container definition but you must pull the image first:

```bash
podman pull quay.io/docling-project/docling-serve
```

### Switching back to Mock

```json
{ "Preprocessing": { "Docling": { "Provider": "Mock" } } }
```
