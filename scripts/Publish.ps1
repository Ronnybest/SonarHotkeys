[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'SonarHotkeys.WinUI\SonarHotkeys.WinUI.csproj'
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
$stagingDirectory = Join-Path $outputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
# Everything goes into one folder inside the archive, so "Extract here" does not scatter files.
$appDirectory = Join-Path $stagingDirectory 'SonarHotkeys'
$archivePath = Join-Path $outputDirectory 'SonarHotkeys-win-x64.zip'

# Native AOT links with MSVC, and its toolchain lookup calls vswhere.exe from PATH.
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
}

New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
try {
    dotnet publish $projectPath -c Release -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    foreach ($document in 'README.md', 'README.ru.md', 'CHANGELOG.md', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $appDirectory
    }
    # The Windows App SDK license requires passing its terms on with the redistributed files.
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'licenses') -Destination $appDirectory -Recurse
    # Native AOT writes a native PDB even with DebugType=None; symbols are not shipped.
    Get-ChildItem -LiteralPath $appDirectory -Recurse -File -Filter '*.pdb' | Remove-Item
    # The staging folder is new, so it only holds publish output; still refuse to ship local settings.
    $unexpected = Get-ChildItem -LiteralPath $appDirectory -Recurse -File |
        Where-Object { $_.Name -in 'settings.json', 'settings.json.tmp' }
    if ($unexpected) { throw "Unexpected files in the release: $($unexpected.FullName -join ', ')" }
    # tar.exe (bsdtar, part of Windows 10+) rather than Compress-Archive or ZipFile: under Windows PowerShell
    # both write backslash entry paths, which other unzip tools extract incorrectly.
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
    & (Join-Path $env:SystemRoot 'System32\tar.exe') -a -c -f $archivePath -C $stagingDirectory SonarHotkeys
    if ($LASTEXITCODE -ne 0) { throw 'Creating the archive failed.' }
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
