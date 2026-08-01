# OpenRAG RAG Smoke Test
# Focuses on upload + RAG ask flow.
# For the comprehensive MVP smoke test, run: ./scripts/mvp-smoke-test.ps1
param(
    [string]$ApiBaseUrl = "https://localhost:7063",
    [string]$FilePath = "README.md",
    [string]$Question = "What is OpenRAG about?",
    [string]$Model = "mock-chat",
    [string]$ExpectedPreprocessor = ""
)

# The -Model parameter controls the requested chat model.
# It only calls a real model if AI:Chat:Provider is set to OpenAICompatible in appsettings.
# The -ExpectedPreprocessor parameter warns if retrieved content looks like mock placeholder text.

$ErrorActionPreference = "Stop"
$uploadUrl = "$ApiBaseUrl/api/documents/upload"
$statusUrlTemplate = "$ApiBaseUrl/api/documents/{0}/status"
$ragUrl = "$ApiBaseUrl/api/rag/ask"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG Smoke Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  API:      $ApiBaseUrl"
Write-Host "  File:     $FilePath"
Write-Host "  Question: $Question"
Write-Host "  Model:    $Model"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Upload
Write-Host "[1/4] Uploading document..." -ForegroundColor Yellow
$upload = curl.exe -s -k $uploadUrl -F "file=@$FilePath;type=text/markdown" 2>&1
if (-not $upload) { throw "Upload failed - is the API running?" }
$json = $upload | ConvertFrom-Json
$docId = $json.documentId
Write-Host "  DocumentId: $docId" -ForegroundColor Green

# 2. Poll status
Write-Host "[2/4] Waiting for pipeline (preprocess -> chunk -> embed)..." -ForegroundColor Yellow
$statusUrl = $statusUrlTemplate -f $docId
$maxWait = 90
$elapsed = 0
$status = "Uploaded"

while ($elapsed -lt $maxWait -and $status -ne "Ready" -and $status -ne "Failed") {
    Start-Sleep -Seconds 3
    $elapsed += 3
    $statusResp = curl.exe -s -k $statusUrl 2>&1
    $statusObj = $statusResp | ConvertFrom-Json
    $status = $statusObj.status
    Write-Host "  ${elapsed}s: $status"
}

if ($status -eq "Failed") {
    Write-Host "  Pipeline FAILED - check Aspire console logs" -ForegroundColor Red
    exit 1
}

# 3. Show detailed status
Write-Host ""
Write-Host "[3/4] Document status:" -ForegroundColor Yellow
$statusResp = curl.exe -s -k $statusUrl 2>&1
$statusJson = $statusResp | ConvertFrom-Json | ConvertTo-Json -Depth 5
Write-Host $statusJson

# Parse details
$statusObj = $statusResp | ConvertFrom-Json
if ($statusObj.versions -and $statusObj.versions.Count -gt 0) {
    $v = $statusObj.versions[0]
    Write-Host ""
    Write-Host "  Version #$($v.versionNumber) Status: $($v.status)"
    Write-Host "  Chunks: $($v.chunkCount)  Embeddings: $($v.embeddingCount)"
    if ($v.embeddingModel) {
        Write-Host "  Embedding: $($v.embeddingProvider)/$($v.embeddingModel) ($($v.embeddingDimensions)d)"
    }
    if ($v.steps) {
        foreach ($s in $v.steps) {
            Write-Host "  Step $($s.name): $($s.status)"
        }
    }
}

# 4. RAG Ask
Write-Host ""
Write-Host "[4/4] RAG Ask (model: $Model)..." -ForegroundColor Yellow
$body = @{ question = $Question; topK = 3; model = $Model } | ConvertTo-Json -Compress
$ragResp = curl.exe -s -k -X POST $ragUrl -H "Content-Type: application/json" -d $body 2>&1
$ragJson = $ragResp | ConvertFrom-Json -Depth 3

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  RAG Results" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Answer preview: $($ragJson.answer.Substring(0, [Math]::Min(150, $ragJson.answer.Length)))..."
Write-Host "  Citations:      $($ragJson.citations.Count)"
Write-Host "  Chunks:         $($ragJson.retrievedChunks.Count)"
Write-Host "  Model:          $($ragJson.model)"

if ($ragJson.retrievedChunks.Count -gt 0) {
    Write-Host ""
    Write-Host "  Top chunks:" -ForegroundColor Green
    $hasMockContent = $false
    foreach ($c in $ragJson.retrievedChunks) {
        Write-Host "    [score=$([math]::Round($c.score, 3))] $($c.content.Substring(0, [Math]::Min(80, $c.content.Length)))..."
        if ($c.content -match "Mock preprocessed content") {
            $hasMockContent = $true
        }
    }
    if ($hasMockContent) {
        Write-Host ""
        if ($ExpectedPreprocessor -eq "DoclingServe") {
            Write-Host "  WARNING: Retrieved chunks contain 'Mock preprocessed content'." -ForegroundColor Red
            Write-Host "  DoclingServe is configured but MockDocumentPreprocessor is still active." -ForegroundColor Red
            Write-Host "  Check Preprocessing:Docling:Provider in appsettings." -ForegroundColor Red
        } else {
            Write-Host "  NOTE: Retrieved chunks contain mock placeholder content." -ForegroundColor DarkYellow
            Write-Host "  Set Preprocessing:Docling:Provider=DoclingServe for real content extraction." -ForegroundColor DarkYellow
        }
    }
    if ($ragJson.citations.Count -gt 0) {
        Write-Host ""
        Write-Host "  Citations:" -ForegroundColor Green
        foreach ($c in $ragJson.citations) {
            Write-Host "    [$($c.index)] $($c.excerpt.Substring(0, [Math]::Min(80, $c.excerpt.Length)))..."
        }
    }
}
Write-Host "========================================" -ForegroundColor Cyan
