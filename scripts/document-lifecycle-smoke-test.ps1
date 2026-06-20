# OpenRAG Document Lifecycle Smoke Test
# Focuses on upload + list/detail/delete flow.
# For the comprehensive MVP smoke test, run: ./scripts/mvp-smoke-test.ps1
param(
    [string]$ApiBaseUrl = "https://localhost:7063",
    [string]$FilePath = "README.md",
    [string]$Question = "What is OpenRAG about?",
    [string]$TenantId = "11111111-1111-1111-1111-111111111111",
    [string]$Model = "mock-chat"
)

$ErrorActionPreference = "Stop"
$uploadUrl = "$ApiBaseUrl/api/documents/upload"
$listUrl = "$ApiBaseUrl/api/documents"
$detailUrlTemplate = "$ApiBaseUrl/api/documents/{0}"
$statusUrlTemplate = "$ApiBaseUrl/api/documents/{0}/status"
$deleteUrlTemplate = "$ApiBaseUrl/api/documents/{0}"
$reprocessUrlTemplate = "$ApiBaseUrl/api/documents/{0}/reprocess"
$ragUrl = "$ApiBaseUrl/api/rag/ask"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG Document Lifecycle Smoke Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── 1. List (should be empty or have existing docs) ────────────────
Write-Host "[1/7] Listing documents..." -ForegroundColor Yellow
$listBefore = Invoke-RestMethod -Uri "$listUrl?pageSize=5" -Method Get -SkipCertificateCheck
Write-Host "  Found $($listBefore.totalCount) document(s)" -ForegroundColor Green

# ── 2. Upload ──────────────────────────────────────────────────────
Write-Host "[2/7] Uploading document..." -ForegroundColor Yellow
$fileBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $FilePath))
$form = @{
    file = [System.Net.Http.MultipartFormDataContent]::new()
}
$fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
$fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
$form['file'].Add($fileContent, "file", (Split-Path $FilePath -Leaf))

$uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method Post -Form $form -SkipCertificateCheck
$documentId = $uploadResponse.documentId
Write-Host "  DocumentId: $documentId" -ForegroundColor Green

# ── 3. Wait until Ready ────────────────────────────────────────────
Write-Host "[3/7] Waiting for processing..." -ForegroundColor Yellow
$statusUrl = $statusUrlTemplate -f $documentId
$maxWait = 60
$waited = 0
do {
    Start-Sleep -Seconds 2
    $waited += 2
    $status = Invoke-RestMethod -Uri $statusUrl -Method Get -SkipCertificateCheck
    Write-Host "  Status: $($status.status) (${waited}s)" -ForegroundColor DarkGray
} while ($status.status -ne "Ready" -and $status.status -ne "Failed" -and $waited -lt $maxWait)

if ($status.status -ne "Ready") {
    Write-Host "  ERROR: Document did not reach Ready (status: $($status.status))" -ForegroundColor Red
    exit 1
}
Write-Host "  Document is Ready" -ForegroundColor Green

# ── 4. List (should include the new doc) ───────────────────────────
Write-Host "[4/7] Listing documents (should include new doc)..." -ForegroundColor Yellow
$listAfter = Invoke-RestMethod -Uri "$listUrl?pageSize=10" -Method Get -SkipCertificateCheck
$found = $listAfter.items | Where-Object { $_.documentId -eq $documentId }
if (-not $found) {
    Write-Host "  WARNING: New document not found in list" -ForegroundColor Magenta
} else {
    Write-Host "  Found: $($found.fileName) ($($found.status)) chunks=$($found.chunkCount) embeddings=$($found.embeddingCount)" -ForegroundColor Green
}

# ── 5. Get detail ──────────────────────────────────────────────────
Write-Host "[5/7] Getting document detail..." -ForegroundColor Yellow
$detailUrl = $detailUrlTemplate -f $documentId
$detail = Invoke-RestMethod -Uri $detailUrl -Method Get -SkipCertificateCheck
Write-Host "  FileName: $($detail.fileName)" -ForegroundColor Green
Write-Host "  Status: $($detail.status)" -ForegroundColor Green
if ($detail.latestVersion) {
    Write-Host "  Version: $($detail.latestVersion.versionId)" -ForegroundColor Green
    Write-Host "  HasSource: $($detail.latestVersion.hasSourceFile)" -ForegroundColor Green
    Write-Host "  HasMarkdown: $($detail.latestVersion.hasMarkdownArtifact)" -ForegroundColor Green
    Write-Host "  Chunks: $($detail.latestVersion.chunkCount)" -ForegroundColor Green
    Write-Host "  Embeddings: $($detail.latestVersion.embeddingCount)" -ForegroundColor Green
}

# ── 6. Ask question ────────────────────────────────────────────────
Write-Host "[6/7] Asking question..." -ForegroundColor Yellow
$askBody = @{
    question = $Question
    tenantId = $TenantId
    topK = 3
    model = $Model
} | ConvertTo-Json
$answer = Invoke-RestMethod -Uri $ragUrl -Method Post -Body $askBody -ContentType "application/json" -SkipCertificateCheck
Write-Host "  Answer: $($answer.answer.Substring(0, [Math]::Min(80, $answer.answer.Length)))..." -ForegroundColor Green

# ── 7. Delete ──────────────────────────────────────────────────────
Write-Host "[7/7] Deleting document..." -ForegroundColor Yellow
$deleteUrl = $deleteUrlTemplate -f $documentId
try {
    Invoke-RestMethod -Uri $deleteUrl -Method Delete -SkipCertificateCheck -StatusCodeVariable sc
    if ($sc -eq 204) {
        Write-Host "  Deleted (204 No Content)" -ForegroundColor Green
    } else {
        Write-Host "  Delete returned $sc" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  Delete failed: $_" -ForegroundColor Red
    exit 1
}

# Verify gone
try {
    $null = Invoke-RestMethod -Uri $detailUrl -Method Get -SkipCertificateCheck
    Write-Host "  WARNING: Document still accessible after delete" -ForegroundColor Magenta
} catch {
    Write-Host "  Verified: Document no longer accessible" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Lifecycle smoke test PASSED" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
