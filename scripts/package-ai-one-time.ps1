param([string]$OutputDirectory = (Join-Path $env:TEMP 'fitmate-ai-one-time-20261110'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'deploy-ai-one-time.sh') -Raw
$files = @()
foreach ($list in @('files', 'test_files')) {
    $match = [regex]::Match($script, '(?m)^' + $list + '=\(([^)]+)\)')
    if (-not $match.Success) { throw "Missing deployment file list: $list" }
    $files += $match.Groups[1].Value.Trim() -split '\s+'
}
foreach ($file in $files) {
    $full = [IO.Path]::GetFullPath((Join-Path $repo $file))
    if (-not $full.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not ($file.StartsWith('src/') -or $file.StartsWith('tests/')) -or
        -not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Invalid or missing reviewed file: $file" }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'overlay.tgz'
Push-Location $repo
try {
    & tar -czf $archive @files
    if ($LASTEXITCODE -ne 0) { throw 'Could not create reviewed overlay.' }
    $contents = @(& tar -tzf $archive)
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect reviewed overlay.' }
    foreach ($file in $files) {
        if ($contents -cnotcontains $file) { throw "Archive omitted required file: $file" }
    }
} finally { Pop-Location }
Write-Output "Verified overlay: $($files.Count) source/test files. No environment or credentials included."
