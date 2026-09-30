[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'SonarHotkeys\SonarHotkeys.csproj'
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
$stagingDirectory = Join-Path $outputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $outputDirectory 'SonarHotkeys-win-x64.zip'

New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
try {
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.ru.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE.txt') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $stagingDirectory
    # Explicit allowlist: never package local settings or other machine-specific files.
    $releaseFiles = @('SonarHotkeys.exe', 'README.md', 'README.ru.md', 'CHANGELOG.md', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md') |
        ForEach-Object { Join-Path $stagingDirectory $_ }
    Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archivePath -Force
    Write-Output "Release archive: $archivePath"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $expectedRoot = [IO.Path]::GetFullPath($outputDirectory) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a staging directory outside artifacts.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
