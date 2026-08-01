param(
    [string]$ApiBaseUrl = "https://localhost:7063",
    [string]$Token = $env:OPENRAG_ACCESS_TOKEN,
    [string]$FilePath = "README.md",
    [string]$Model = "deepseek-chat",
    [string]$Question = "What is this document about?",
    [string]$ExpectedPreprocessor = "",
    [switch]$SkipDelete,
    [int]$TimeoutSeconds = 180,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
OpenRAG MVP Smoke Test
======================
Runs the full API-first MVP flow: upload, process, inspect, RAG ask,
reprocess, ask again, and delete.

Parameters:
  -ApiBaseUrl          API base URL (default: https://localhost:7063)
  -Token               JWT access token with GUID user/tenant claims and admin role (defaults to OPENRAG_ACCESS_TOKEN)
  -FilePath            Document to upload (default: README.md)
  -Model               Chat model for RAG ask (default: deepseek-chat)
  -Question            Question for RAG ask (default: "What is this document about?")
  -ExpectedPreprocessor Expected preprocessor name for validation (e.g., DoclingServe)
  -SkipDelete          Skip the delete step at the end
  -TimeoutSeconds      Max seconds to wait for processing (default: 180)
  -Help                Show this help

Examples:
  ./scripts/mvp-smoke-test.ps1 -Token `$env:OPENRAG_ACCESS_TOKEN
  ./scripts/mvp-smoke-test.ps1 -Token `$env:OPENRAG_ACCESS_TOKEN -Model "deepseek-chat" -ExpectedPreprocessor "DoclingServe"
  ./scripts/mvp-smoke-test.ps1 -Token `$env:OPENRAG_ACCESS_TOKEN -FilePath "testdoc.md" -SkipDelete
"@
    exit 0
}

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "A JWT access token with valid user, tenant, and admin claims is required. Pass -Token or set OPENRAG_ACCESS_TOKEN."
}

$authorizationHeaders = @{ Authorization = "Bearer $Token" }

# ═══════════════════════════════════════════════════════════════════
# Header
# ═══════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OpenRAG MVP Smoke Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  API:             $ApiBaseUrl"
Write-Host "  File:            $FilePath"
Write-Host "  Question:        $Question"
Write-Host "  Model:           $Model"
Write-Host "  Timeout:         ${TimeoutSeconds}s"
Write-Host "  SkipDelete:      $SkipDelete"
Write-Host "  ExpectedPreproc: $ExpectedPreprocessor"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Build a safe file name for upload
$fileName = Split-Path $FilePath -Leaf
$fileBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $FilePath))

# Helper: PUT/POST with form data
function Invoke-Upload {
    param([string]$Url, [string]$FileName, [byte[]]$FileBytes)
    $form = @{ file = [System.Net.Http.MultipartFormDataContent]::new() }
    $fileContent = [System.Net.Http.ByteArrayContent]::new($FileBytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
    $form['file'].Add($fileContent, "file", $FileName)
    return Invoke-RestMethod -Uri $Url -Method Post -Form $form -Headers $authorizationHeaders -SkipCertificateCheck
}

# Helper: safe JSON body
function Invoke-JsonPost {
    param([string]$Url, $Body)
    $json = $Body | ConvertTo-Json -Compress
    return Invoke-RestMethod -Uri $Url -Method Post -Body $json -ContentType "application/json" -Headers $authorizationHeaders -SkipCertificateCheck
}

# Helper: wait for document status
function Wait-ForStatus {
    param([string]$StatusUrl, [string[]]$TargetStatuses, [string]$Label)
    $waited = 0
    $interval = 2
    do {
        Start-Sleep -Seconds $interval
        $waited += $interval
        $status = Invoke-RestMethod -Uri $StatusUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck -ErrorAction SilentlyContinue
        if (-not $status) {
            Write-Host "  [${waited}s] Status request failed, retrying..." -ForegroundColor DarkGray
            continue
        }
        $current = $status.status
        Write-Host "  [${waited}s] $current" -ForegroundColor DarkGray
        if ($current -in $TargetStatuses) { return $status }
        if ($current -eq "Failed") {
            Write-Host "  ERROR: $Label reached Failed state." -ForegroundColor Red
            Write-Host "  Status response: $($status | ConvertTo-Json -Depth 4)"
            throw "$Label failed (status: Failed)"
        }
    } while ($waited -lt $TimeoutSeconds)
    throw "$Label timed out after ${TimeoutSeconds}s (last status: $($status.status))"
}

# ═══════════════════════════════════════════════════════════════════
# Step 1 — Provider diagnostics
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [1] Provider diagnostics ━━━" -ForegroundColor Yellow
try {
    $providers = Invoke-RestMethod -Uri "$ApiBaseUrl/api/system/providers" -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
    Write-Host "  Preprocessing : $($providers.preprocessing.provider) (configured: $($providers.preprocessing.configured))"
    if ($providers.preprocessing.baseUrl) { Write-Host "    BaseUrl: $($providers.preprocessing.baseUrl)" }
    if ($providers.preprocessing.validationErrors) {
        foreach ($e in $providers.preprocessing.validationErrors) {
            Write-Host "    WARNING: $e" -ForegroundColor DarkYellow
        }
    }
    Write-Host "  Chunking      : $($providers.chunking.provider) (max: $($providers.chunking.maxChunkCharacters), overlap: $($providers.chunking.overlapCharacters))"
    Write-Host "  Embeddings    : $($providers.embeddings.provider) (model: $($providers.embeddings.model), baseUrl: $($providers.embeddings.baseUrl), apiKeyPresent: $($providers.embeddings.apiKeyPresent))"
    Write-Host "  Chat          : $($providers.chat.provider) (model: $($providers.chat.model), baseUrl: $($providers.chat.baseUrl), apiKeyPresent: $($providers.chat.apiKeyPresent))"
    Write-Host "  Storage       : $($providers.storage.provider) (path: $($providers.storage.localRootPath))"

    if ($ExpectedPreprocessor -and $providers.preprocessing.provider -ne $ExpectedPreprocessor) {
        Write-Host "  WARNING: Expected preprocessor '$ExpectedPreprocessor' but found '$($providers.preprocessing.provider)'" -ForegroundColor DarkYellow
    }
} catch {
    Write-Host "  WARNING: Provider diagnostics not available: $_" -ForegroundColor DarkYellow
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 2 — Upload
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [2] Upload document ━━━" -ForegroundColor Yellow
$upload = Invoke-Upload -Url "$ApiBaseUrl/api/documents/upload" -FileName $fileName -FileBytes $fileBytes
$documentId = $upload.documentId
$versionId = $upload.versionId
Write-Host "  DocumentId : $documentId" -ForegroundColor Green
Write-Host "  VersionId  : $versionId" -ForegroundColor Green
Write-Host "  Status     : $($upload.status)" -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 3 — Wait until Ready
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [3] Wait for processing → Ready ━━━" -ForegroundColor Yellow
$statusUrl = "$ApiBaseUrl/api/documents/$documentId/status"
$status = Wait-ForStatus -StatusUrl $statusUrl -TargetStatuses @("Ready") -Label "Initial processing"
Write-Host "  Document is Ready" -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 4 — Status with processing history
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [4] Document status (with processing history) ━━━" -ForegroundColor Yellow
$status = Invoke-RestMethod -Uri $statusUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
Write-Host "  Status         : $($status.status)"
Write-Host "  FileName       : $($status.originalFileName)"
if ($status.versions -and $status.versions.Count -gt 0) {
    $v = $status.versions[0]
    Write-Host "  Version        : $($v.versionId)"
    Write-Host "  VersionStatus  : $($v.status)"
    Write-Host "  Chunks         : $($v.chunkCount)"
    Write-Host "  Embeddings     : $($v.embeddingCount)"
    if ($v.embeddingProvider) {
        Write-Host "  Embedding      : $($v.embeddingProvider) / $($v.embeddingModel) ($($v.embeddingDimensions)d)"
    }
}
if ($status.processingRuns -and $status.processingRuns.Count -gt 0) {
    Write-Host "  Processing runs:"
    foreach ($run in $status.processingRuns) {
        Write-Host "    Run $($run.runId | ForEach-Object { $_.ToString().Substring(0,8) })... reason=$($run.reason) status=$($run.status)"
        if ($run.steps) {
            foreach ($step in $run.steps) {
                $icon = if ($step.status -eq "Completed") { "[OK]" } elseif ($step.status -eq "Failed") { "[FAIL]" } else { "[..]" }
                Write-Host "      $icon $($step.name): $($step.status) (attempts: $($step.attemptCount))"
                if ($step.errorMessage) {
                    Write-Host "        Error: $($step.errorMessage)" -ForegroundColor Red
                }
            }
        }
    }
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 5 — List documents
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [5] List documents ━━━" -ForegroundColor Yellow
$list = Invoke-RestMethod -Uri "$ApiBaseUrl/api/documents?pageSize=10" -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
$foundDoc = $list.items | Where-Object { $_.documentId -eq $documentId }
if ($foundDoc) {
    Write-Host "  Found in list: $($foundDoc.fileName) status=$($foundDoc.status)" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Document $documentId not found in list" -ForegroundColor DarkYellow
}
Write-Host "  Total documents: $($list.totalCount)" -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 6 — Document detail
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [6] Document detail ━━━" -ForegroundColor Yellow
$detailUrl = "$ApiBaseUrl/api/documents/$documentId"
$detail = Invoke-RestMethod -Uri $detailUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
Write-Host "  FileName       : $($detail.fileName)"
Write-Host "  Status         : $($detail.status)"
if ($detail.latestVersion) {
    $lv = $detail.latestVersion
    Write-Host "  VersionId      : $($lv.versionId)"
    Write-Host "  VersionNumber  : $($lv.versionNumber)"
    Write-Host "  HasSource      : $($lv.hasSourceFile)"
    Write-Host "  HasMarkdown    : $($lv.hasMarkdownArtifact)"
    Write-Host "  HasJson        : $($lv.hasJsonArtifact)"
    Write-Host "  Chunks         : $($lv.chunkCount)"
    Write-Host "  Embeddings     : $($lv.embeddingCount)"
    if ($lv.embeddingProvider) {
        Write-Host "  Embedding      : $($lv.embeddingProvider) / $($lv.embeddingModel) ($($lv.embeddingDimensions)d)"
    }
}
Write-Host ""

# Resolve version ID for artifact/chunk endpoints
$versionId = if ($detail.latestVersion.versionId) { $detail.latestVersion.versionId } else { $versionId }

# ═══════════════════════════════════════════════════════════════════
# Step 7 — Markdown artifact
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [7] Markdown artifact ━━━" -ForegroundColor Yellow
$mdUrl = "$ApiBaseUrl/api/documents/$documentId/versions/$versionId/artifacts/markdown"
try {
    $markdown = Invoke-RestMethod -Uri $mdUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
    $mdPreview = $markdown.Substring(0, [Math]::Min(200, $markdown.Length))
    Write-Host "  Preview: $($mdPreview -replace '\n', ' ' | ForEach-Object { $_.Substring(0, [Math]::Min(150, $_.Length)) })..." -ForegroundColor Green
} catch {
    Write-Host "  WARNING: Could not fetch Markdown artifact: $_" -ForegroundColor DarkYellow
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 8 — JSON artifact
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [8] JSON artifact ━━━" -ForegroundColor Yellow
$jsonUrl = "$ApiBaseUrl/api/documents/$documentId/versions/$versionId/artifacts/json"
try {
    $jsonArtifact = Invoke-RestMethod -Uri $jsonUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
    $jsonPreview = $jsonArtifact.Substring(0, [Math]::Min(150, $jsonArtifact.Length))
    Write-Host "  Preview: $jsonPreview..." -ForegroundColor Green
} catch {
    Write-Host "  NOTE: JSON artifact not available (may be expected with Mock preprocessor)" -ForegroundColor DarkGray
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 9 — List chunks
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [9] List chunks ━━━" -ForegroundColor Yellow
$chunksUrl = "$ApiBaseUrl/api/documents/$documentId/versions/$versionId/chunks?pageSize=5"
$chunks = Invoke-RestMethod -Uri $chunksUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
Write-Host "  Total chunks: $($chunks.totalCount)" -ForegroundColor Green
$firstChunkId = $null
if ($chunks.items -and $chunks.items.Count -gt 0) {
    $first = $chunks.items[0]
    $firstChunkId = $first.chunkId
    $contentPreview = $first.content.Substring(0, [Math]::Min(120, $first.content.Length))
    Write-Host "  First chunk : index=$($first.chunkIndex) page=$($first.pageNumber) section=$($first.sectionTitle)" -ForegroundColor Green
    Write-Host "    Content   : $contentPreview..." -ForegroundColor Green
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 10 — First chunk detail
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [10] First chunk detail ━━━" -ForegroundColor Yellow
if ($firstChunkId) {
    $chunkDetailUrl = "$ApiBaseUrl/api/documents/$documentId/versions/$versionId/chunks/$firstChunkId"
    $chunkDetail = Invoke-RestMethod -Uri $chunkDetailUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck
    Write-Host "  ChunkId    : $($chunkDetail.chunkId)"
    Write-Host "  Index      : $($chunkDetail.chunkIndex)"
    Write-Host "  Page       : $($chunkDetail.pageNumber)"
    Write-Host "  Section    : $($chunkDetail.sectionTitle)"
    Write-Host "  Tokens     : $($chunkDetail.tokenCount)"
    if ($chunkDetail.embeddingProvider) {
        Write-Host "  Embedding  : $($chunkDetail.embeddingProvider) / $($chunkDetail.embeddingModel) ($($chunkDetail.embeddingDimensions)d)" -ForegroundColor Green
    }
} else {
    Write-Host "  No chunks to inspect" -ForegroundColor DarkGray
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 11 — Ask question
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [11] RAG Ask (model: $Model) ━━━" -ForegroundColor Yellow
$askBody = @{
    question   = $Question
    topK       = 3
    model      = $Model
}
$answer = Invoke-JsonPost -Url "$ApiBaseUrl/api/rag/ask" -Body $askBody
$ansPreview = $answer.answer.Substring(0, [Math]::Min(200, $answer.answer.Length))
Write-Host "  Answer    : $ansPreview..." -ForegroundColor Green
Write-Host "  Citations : $($answer.citations.Count)" -ForegroundColor Green
Write-Host "  Chunks    : $($answer.retrievedChunks.Count)" -ForegroundColor Green
Write-Host "  Model     : $($answer.model)" -ForegroundColor Green

if ($answer.retrievedChunks -and $answer.retrievedChunks.Count -gt 0) {
    Write-Host "  Top retrieved chunks:"
    foreach ($c in $answer.retrievedChunks) {
        $cp = $c.content.Substring(0, [Math]::Min(80, $c.content.Length))
        Write-Host "    [score=$([math]::Round($c.score, 3))] $cp..."
    }
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 12 — Reprocess
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [12] Reprocess document ━━━" -ForegroundColor Yellow
$reprocessUrl = "$ApiBaseUrl/api/documents/$documentId/reprocess"
$reprocessBody = @{
    forcePreprocess  = $true
    forceChunk       = $true
    forceEmbeddings  = $true
}
$reprocess = Invoke-JsonPost -Url $reprocessUrl -Body $reprocessBody
Write-Host "  Status        : $($reprocess.status)" -ForegroundColor Green
Write-Host "  CorrelationId : $($reprocess.correlationId)" -ForegroundColor Green
if ($reprocess.status -ne "Processing") {
    Write-Host "  ERROR: Expected 'Processing', got '$($reprocess.status)'" -ForegroundColor Red
    throw "Reprocess failed"
}
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 13 — Wait until Ready again
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [13] Wait for reprocessing → Ready ━━━" -ForegroundColor Yellow
$status = Wait-ForStatus -StatusUrl $statusUrl -TargetStatuses @("Ready") -Label "Reprocessing"
Write-Host "  Document is Ready again" -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 14 — Ask again
# ═══════════════════════════════════════════════════════════════════
Write-Host "━━━ [14] RAG Ask after reprocess (model: $Model) ━━━" -ForegroundColor Yellow
$answer2 = Invoke-JsonPost -Url "$ApiBaseUrl/api/rag/ask" -Body $askBody
$ans2Preview = $answer2.answer.Substring(0, [Math]::Min(200, $answer2.answer.Length))
Write-Host "  Answer    : $ans2Preview..." -ForegroundColor Green
Write-Host "  Citations : $($answer2.citations.Count)" -ForegroundColor Green
Write-Host "  Chunks    : $($answer2.retrievedChunks.Count)" -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════
# Step 15 — Delete
# ═══════════════════════════════════════════════════════════════════
if (-not $SkipDelete) {
    Write-Host "━━━ [15] Delete document ━━━" -ForegroundColor Yellow
    $deleteUrl = "$ApiBaseUrl/api/documents/$documentId"
    try {
        Invoke-RestMethod -Uri $deleteUrl -Method Delete -Headers $authorizationHeaders -SkipCertificateCheck -StatusCodeVariable deleteSc
        if ($deleteSc -eq 204) {
            Write-Host "  Deleted (204 No Content)" -ForegroundColor Green
        } else {
            Write-Host "  Delete returned HTTP $deleteSc" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ERROR: Delete failed: $_" -ForegroundColor Red
        throw "Delete failed"
    }
    Write-Host ""

    # ═══════════════════════════════════════════════════════════════
    # Step 16 — Confirm 404 after delete
    # ═══════════════════════════════════════════════════════════════
    Write-Host "━━━ [16] Confirm deleted document returns 404 ━━━" -ForegroundColor Yellow
    $verifiedGone = $true

    # Detail
    try {
        $null = Invoke-RestMethod -Uri $detailUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck -StatusCodeVariable detailSc2
        if ($detailSc2 -ne 404) {
            Write-Host "  WARNING: Detail returned $detailSc2 (expected 404)" -ForegroundColor DarkYellow
            $verifiedGone = $false
        } else {
            Write-Host "  Detail: 404 as expected" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Detail: 404 as expected (exception)" -ForegroundColor Green
    }

    # Status
    try {
        $null = Invoke-RestMethod -Uri $statusUrl -Method Get -Headers $authorizationHeaders -SkipCertificateCheck -StatusCodeVariable statusSc2
        if ($statusSc2 -ne 404) {
            Write-Host "  WARNING: Status returned $statusSc2 (expected 404)" -ForegroundColor DarkYellow
            $verifiedGone = $false
        } else {
            Write-Host "  Status: 404 as expected" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Status: 404 as expected (exception)" -ForegroundColor Green
    }

    if (-not $verifiedGone) {
        Write-Host "  WARNING: Deletion verification incomplete" -ForegroundColor DarkYellow
    }
    Write-Host ""
} else {
    Write-Host "━━━ [15-16] Delete skipped (--SkipDelete) ━━━" -ForegroundColor DarkGray
    Write-Host ""
}

# ═══════════════════════════════════════════════════════════════════
# Final summary
# ═══════════════════════════════════════════════════════════════════
Write-Host "========================================" -ForegroundColor Green
Write-Host "  MVP Smoke Test PASSED" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  DocumentId : $documentId" -ForegroundColor Green
Write-Host "  VersionId  : $versionId" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
