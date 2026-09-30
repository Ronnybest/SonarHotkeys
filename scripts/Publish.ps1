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
$installerProject = Join-Path $repositoryRoot 'installer\SonarHotkeys.Installer.wixproj'
$installerPath = Join-Path $outputDirectory 'SonarHotkeys-win-x64.msi'
$version = ([xml](Get-Content -LiteralPath $projectPath -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

# Plain text to RTF for the installer's license page: escapes RTF syntax and writes non-ASCII as \u.
function ConvertTo-RtfText([string]$Text) {
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $Text.Replace("`r`n", "`n").ToCharArray()) {
        $code = [int]$character
        if ($character -eq '\' -or $character -eq '{' -or $character -eq '}') { [void]$builder.Append('\').Append($character) }
        elseif ($character -eq "`n") { [void]$builder.Append("\par`r`n") }
        elseif ($code -gt 127) { if ($code -gt 32767) { $code -= 65536 }; [void]$builder.Append("\u$code?") }
        else { [void]$builder.Append($character) }
    }
    $builder.ToString()
}

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

    # The license page shows this app's license and the terms of the redistributed Microsoft components.
    # It is written next to the app folder, not into it, so the installer does not install it.
    $licenseRtf = Join-Path $stagingDirectory 'License.rtf'
    $sections = @(
        @('SonarHotkeys', 'LICENSE.txt'),
        @('Microsoft Windows App SDK', 'licenses\WindowsAppSDK-LICENSE.txt'),
        @('Microsoft Windows App SDK notices', 'licenses\WindowsAppSDK-NOTICE.txt'),
        @('Microsoft Edge WebView2', 'licenses\WebView2-LICENSE.txt'))
    $body = foreach ($section in $sections) {
        '\b ' + (ConvertTo-RtfText $section[0]) + '\b0\par\par' + "`r`n" + (ConvertTo-RtfText (Get-Content -LiteralPath (Join-Path $repositoryRoot $section[1]) -Raw -Encoding UTF8)) + '\par' + "`r`n"
    }
    [IO.File]::WriteAllText($licenseRtf, '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs17' + "`r`n" + ($body -join '') + '}', [Text.Encoding]::ASCII)

    # BindPath needs the trailing separator for !(bindpath.App)** to harvest the folder's contents.
    dotnet build $installerProject -c Release "-p:AppDir=$appDirectory\" "-p:ProductVersion=$version" "-p:LicenseRtf=$licenseRtf" -o (Join-Path $stagingDirectory 'msi')
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    Copy-Item -LiteralPath (Join-Path $stagingDirectory 'msi\SonarHotkeys-win-x64.msi') -Destination $installerPath -Force
    Write-Output "Installer: $installerPath"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $expectedRoot = [IO.Path]::GetFullPath($outputDirectory) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a staging directory outside artifacts.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
