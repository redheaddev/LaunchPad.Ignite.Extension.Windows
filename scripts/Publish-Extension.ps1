[CmdletBinding()]
param(
    [string]$Version,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectName = 'LaunchPad.Ignite.Extension.Windows'
$projectPath = Join-Path $repositoryRoot "$projectName.csproj"
$manifestPath = Join-Path $repositoryRoot 'extension.json'

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Extension project was not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Extension manifest was not found: $manifestPath"
}

$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json -ErrorAction Stop
if (-not [string]::Equals([string]$manifest.id, 'launchpad-windows', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Manifest ID '$($manifest.id)' does not match the Windows extension."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$manifest.version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' is invalid. Specify numeric major.minor.patch, such as 1.2.3."
}

$versionRegex = [regex]::new('(?m)^(\s*"version"\s*:\s*")[^"]*(".*)$')
if ($versionRegex.Matches($manifestText).Count -ne 1) {
    throw "Expected exactly one root version property in '$manifestPath'."
}
$updatedManifestText = $versionRegex.Replace(
    $manifestText,
    [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return $match.Groups[1].Value + $Version + $match.Groups[2].Value
    },
    1)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("LaunchPad.Ignite.windows.{0}" -f [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $temporaryDirectory 'publish'
$packageStagingDirectory = Join-Path $temporaryDirectory 'package'
$assemblyVersion = "$Version.0"
$packagePath = Join-Path $OutputDirectory "launchpad-windows-$Version.zip"

try {
    Push-Location $repositoryRoot
    try {
        & dotnet publish $projectPath `
            --configuration $Configuration `
            --output $publishDirectory `
            --nologo `
            "-p:Version=$Version" `
            "-p:AssemblyVersion=$assemblyVersion" `
            "-p:FileVersion=$assemblyVersion" `
            "-p:InformationalVersion=$Version" `
            '-p:IncludeSourceRevisionInInformationalVersion=false'
        if ($LASTEXITCODE -ne 0) {
            throw "Publishing '$projectName' failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    $assemblyPath = Join-Path $publishDirectory "$projectName.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Published extension assembly was not found: $assemblyPath"
    }

    [System.IO.Directory]::CreateDirectory($packageStagingDirectory) | Out-Null
    Get-ChildItem -LiteralPath $publishDirectory -File |
        Where-Object { $_.Extension -ne '.pdb' } |
        Copy-Item -Destination $packageStagingDirectory
    [System.IO.File]::WriteAllText(
        (Join-Path $packageStagingDirectory 'extension.json'),
        $updatedManifestText,
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    Compress-Archive -Path (Join-Path $packageStagingDirectory '*') -DestinationPath $packagePath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

Write-Host "Published Windows extension $Version to $packagePath"
