# OpenRAG Reprocess Smoke Test
# Focuses on upload + reprocess flow.
# For the comprehensive MVP smoke test, run: ./scripts/mvp-smoke-test.ps1
param(
    [string]$ApiBaseUrl = "https://localhost:7063",
    [string]$FilePath = "README.md",
    [string]$Question = "What is OpenRAG about?",
    [string]$Model = "mock-chat"
)

$ErrorActionPreference = "Stop"
$uploadUrl = "$ApiBaseUrl/api/documents/upload"
$statusUrlTemplate = "$ApiBaseUrl/api/documents/{0}/status"
$reprocessUrlTemplate = "$ApiBaseUrl/api/documents/{0}/reprocess"
$ragUrl = "$ApiBaseUrl/api/rag/ask"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG Reprocess Smoke Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  API:      $ApiBaseUrl"
Write-Host "  File:     $FilePath"
Write-Host "  Question: $Question"
Write-Host "  Model:    $Model"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Upload
Write-Host "[1/6] Uploading document..." -ForegroundColor Yellow
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

# 2. Wait until Ready
Write-Host "[2/6] Waiting for processing to complete..." -ForegroundColor Yellow
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
    Write-Host "  ERROR: Document did not reach Ready state (status: $($status.status))" -ForegroundColor Red
    exit 1
}
Write-Host "  Document is Ready" -ForegroundColor Green

# 3. Ask question (pre-reprocess baseline)
Write-Host "[3/6] Asking question (pre-reprocess)..." -ForegroundColor Yellow
$askBody = @{
    question = $Question
    topK = 3
    model = $Model
} | ConvertTo-Json

$answer1 = Invoke-RestMethod -Uri $ragUrl -Method Post -Body $askBody -ContentType "application/json" -SkipCertificateCheck
Write-Host "  Answer: $($answer1.answer.Substring(0, [Math]::Min(80, $answer1.answer.Length)))..." -ForegroundColor Green

# 4. Reprocess
Write-Host "[4/6] Reprocessing document..." -ForegroundColor Yellow
$reprocessUrl = $reprocessUrlTemplate -f $documentId
$reprocessBody = @{
    forcePreprocess = $true
    forceChunk = $true
    forceEmbeddings = $true
} | ConvertTo-Json

$reprocessResponse = Invoke-RestMethod -Uri $reprocessUrl -Method Post -Body $reprocessBody -ContentType "application/json" -SkipCertificateCheck
Write-Host "  Status: $($reprocessResponse.status)" -ForegroundColor Yellow
Write-Host "  VersionId: $($reprocessResponse.versionId)" -ForegroundColor Yellow
Write-Host "  CorrelationId: $($reprocessResponse.correlationId)" -ForegroundColor Yellow

if ($reprocessResponse.status -ne "Processing") {
    Write-Host "  ERROR: Expected status 'Processing', got '$($reprocessResponse.status)'" -ForegroundColor Red
    exit 1
}

# 5. Wait until Ready again
Write-Host "[5/6] Waiting for reprocessing to complete..." -ForegroundColor Yellow
$waited = 0
do {
    Start-Sleep -Seconds 2
    $waited += 2
    $status = Invoke-RestMethod -Uri $statusUrl -Method Get -SkipCertificateCheck
    Write-Host "  Status: $($status.status) (${waited}s)" -ForegroundColor DarkGray
} while ($status.status -ne "Ready" -and $status.status -ne "Failed" -and $waited -lt $maxWait)

if ($status.status -ne "Ready") {
    Write-Host "  ERROR: Document did not reach Ready after reprocess (status: $($status.status))" -ForegroundColor Red
    exit 1
}
Write-Host "  Document is Ready again" -ForegroundColor Green

# 6. Ask question again
Write-Host "[6/6] Asking question (post-reprocess)..." -ForegroundColor Yellow
$answer2 = Invoke-RestMethod -Uri $ragUrl -Method Post -Body $askBody -ContentType "application/json" -SkipCertificateCheck
Write-Host "  Answer: $($answer2.answer.Substring(0, [Math]::Min(80, $answer2.answer.Length)))..." -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Reprocess smoke test PASSED" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
