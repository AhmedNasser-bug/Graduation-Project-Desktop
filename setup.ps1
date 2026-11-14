param(
    [switch]$Docker,
    [switch]$VerifyOnly,
    [switch]$SkipTests,
    [string]$BackendDir = "",
    [string]$BackendBranch = "feat/analytics-sync-refactor"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$repoParent = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BackendDir)) {
    $BackendDir = Join-Path $repoParent "backend"
}

function Ensure-CompatibleBackend {
    param([string]$TargetDir, [string]$FallbackBranch)

    $servicesProj = Join-Path $TargetDir "Services_Unit\Services_Unit.csproj"
    if (-not (Test-Path $servicesProj)) {
        Write-Host "[Desktop Setup] Sibling backend missing at '$TargetDir'. Cloning xByteA/Graduation-Project-Backend ..."
        git clone https://github.com/xByteA/Graduation-Project-Backend.git $TargetDir
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to clone xByteA/Graduation-Project-Backend into $TargetDir. Ensure GitHub access and retry."
        }
    }

    # Desktop currently depends on analytics/sync contracts introduced in feat/analytics-sync-refactor until merged to main
    $handshakeMarker = Join-Path $TargetDir "Persistence_Unit\Interfaces\IOrderHandshakeRepository.cs"
    if (-not (Test-Path $handshakeMarker)) {
        Write-Host "[Desktop Setup] Fetching '$FallbackBranch' inside '$TargetDir' for compatible Services/Persistence contracts ..."
        git -C $TargetDir fetch origin $FallbackBranch
        if ($LASTEXITCODE -eq 0) {
            git -C $TargetDir checkout $FallbackBranch
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to checkout backend branch '$FallbackBranch'."
            }
        }
    }
}

Ensure-CompatibleBackend -TargetDir $BackendDir -FallbackBranch $BackendBranch

if ($Docker) {
    $stageDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ng-desktop-ctx-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
    try {
        Write-Host "[Desktop Setup] Staging clean minimal Docker context at $stageDir ..."
        New-Item -ItemType Directory -Path "$stageDir\backend","$stageDir\desktop" -Force | Out-Null

        foreach ($sub in @("Persistence_Unit","Middleware_Unit","Services_Unit")) {
            robocopy (Join-Path $BackendDir $sub) "$stageDir\backend\$sub" /E /XD bin obj .git TestArtifacts TestResults | Out-Null
        }
        $mendeleyDir = Join-Path $BackendDir "Tests_Unit\Test_Data"
        if (Test-Path $mendeleyDir) {
            robocopy $mendeleyDir "$stageDir\backend\Tests_Unit\Test_Data" /E | Out-Null
        }

        robocopy $PSScriptRoot "$stageDir\desktop" /E /XD bin obj .git .vs TestArtifacts TestResults RuntimeStores | Out-Null

        Write-Host "[Desktop Setup] Building Docker test image 'nephroguard-desktop-tests' ..."
        docker build -f "$stageDir\desktop\Dockerfile" -t nephroguard-desktop-tests $stageDir
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        Write-Host "[Desktop Setup] Executing Headless UI + Clinical Safety Suite inside container ..."
        docker run --rm nephroguard-desktop-tests
        exit $LASTEXITCODE
    } finally {
        if (Test-Path $stageDir) {
            Remove-Item -Path $stageDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$dotnetVersion = & dotnet --version
Write-Host "[Desktop Setup] Using .NET SDK $dotnetVersion"

Write-Host "[Desktop Setup] Restoring & building NephroEdge.Desktop.slnx ..."
dotnet build NephroEdge.Desktop.slnx /p:UseSharedCompilation=false -nodeReuse:false -maxCpuCount:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    Write-Host "[Desktop Setup] Running 41-check Avalonia Headless UI + Clinical Safety Suite ..."
    dotnet run --project NephroEdge.Desktop.HeadlessTests --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($VerifyOnly) {
    Write-Host "[Desktop Setup] Verification completed cleanly."
    exit 0
}

Write-Host "[Desktop Setup] Launching NephroEdge Clinic Workstation ..."
dotnet run --project NephroEdge.Desktop --no-build
