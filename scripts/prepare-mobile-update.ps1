# Prepare/upload reviewed source only; the operator runs the privileged rollout separately.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskKey = Join-Path $env:USERPROFILE '.ssh\fitmate_vps_ed25519'
$taskRemote = 'fitmate@103.116.39.29'
$taskRelease = '/home/fitmate/releases/mobile-update-20261026'
$taskArtifacts = Join-Path $env:TEMP ('fitmate-backend-update-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskArtifacts | Out-Null
Push-Location $taskRoot
try {
    # Archive tracked source + test contract; never recurse over arbitrary .env/config or build outputs.
    $taskInputs = @(& git ls-files -- src tests global.json Fitmate.slnx Dockerfile.migrate .dockerignore .gitleaks.toml docs/backend-release/openapi/fitmate-v1.yaml)
    if ($LASTEXITCODE -ne 0 -or $taskInputs.Count -lt 100) { throw 'Cannot enumerate tracked source.' }
    # This reviewed media fix has not been committed yet; include its new source and test explicitly.
    $taskInputs += @(
        'src/Fitmate.Application/Workout/WorkoutMediaReference.cs',
        'tests/Fitmate.UnitTests/WorkoutMediaReferenceTests.cs',
        'src/Fitmate.Application/Moderation/ReportEvidenceService.cs',
        'src/Fitmate.Infrastructure/Moderation/EfReportEvidenceStore.cs',
        'tests/Fitmate.UnitTests/ReportEvidenceServiceTests.cs'
    )
    $taskInputs = @($taskInputs | Sort-Object -Unique)
    foreach ($taskInput in $taskInputs) {
        if (-not (Test-Path -LiteralPath $taskInput -PathType Leaf)) { throw "Missing source file: $taskInput" }
    }
    if ($taskInputs | Where-Object { $_ -match '(^|/)\.env($|/)|\.(pfx|p12|keystore|jks|dump)$|(^|/)(bin|obj)/' }) {
        throw 'Refusing unexpected secret/generated path in source manifest.'
    }
    $taskManifest = Join-Path $taskArtifacts 'manifest.txt'
    [IO.File]::WriteAllLines($taskManifest, $taskInputs, [Text.UTF8Encoding]::new($false))
    $taskBundle = Join-Path $taskArtifacts 'source.tgz'
    & tar -czf $taskBundle -T $taskManifest
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $taskHash = (Get-FileHash -LiteralPath $taskBundle -Algorithm SHA256).Hash.ToLowerInvariant()
    & ssh -o BatchMode=yes -o ConnectTimeout=10 -i $taskKey $taskRemote "mkdir -p $taskRelease"
    if ($LASTEXITCODE -ne 0) { throw 'SSH failed. Load the VPS key with ssh-add first.' }
    & scp -o BatchMode=yes -i $taskKey $taskBundle "${taskRemote}:$taskRelease/source.tgz"
    if ($LASTEXITCODE -ne 0) { throw 'Source upload failed.' }
    & scp -o BatchMode=yes -i $taskKey (Join-Path $PSScriptRoot 'deploy-mobile-update.sh') (Join-Path $PSScriptRoot 'audit-production-config.sh') "${taskRemote}:$taskRelease/"
    if ($LASTEXITCODE -ne 0) { throw 'Script upload failed.' }
    & ssh -o BatchMode=yes -i $taskKey $taskRemote "cd $taskRelease && printf '%s  source.tgz\n' '$taskHash' | sha256sum --check && bash -n deploy-mobile-update.sh && bash -n audit-production-config.sh"
    if ($LASTEXITCODE -ne 0) { throw 'Remote hash/syntax verification failed. Do not deploy.' }
    Write-Host 'MOBILE_BACKEND_UPDATE_STAGED (not deployed)'
    Write-Host "Archive retained locally: $taskBundle"
    Write-Host "Operator: ssh -t -i `"$taskKey`" $taskRemote `"sudo bash $taskRelease/deploy-mobile-update.sh`""
} finally { Pop-Location }
