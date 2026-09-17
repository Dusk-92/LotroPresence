param(
    [string]$ExpectedTag = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$BridgeProject = Join-Path $RepositoryRoot "bridge/LotroPresence.Bridge/LotroPresence.Bridge.csproj"
$TestsProject = Join-Path $RepositoryRoot "tests/LotroPresence.Tests/LotroPresence.Tests.csproj"
$PublishDirectory = Join-Path $RepositoryRoot "publish/LotroPresence"
$ZipPath = Join-Path $RepositoryRoot "LotroPresence-win-x64.zip"
$ChecksumPath = "$ZipPath.sha256"

Push-Location $RepositoryRoot
try {
    $Version = (Get-Content -Raw "VERSION").Trim()
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VERSION invalide : $Version"
    }

    $Manifest = [xml](Get-Content -Raw "plugin/Dusk/LotroPresence.plugin")
    $ManifestVersion = [string]$Manifest.Plugin.Information.Version
    $Lua = Get-Content -Raw "plugin/Dusk/LotroPresence/Main.lua"
    if ($Lua -notmatch 'local VERSION = "([^"]+)";') {
        throw "Impossible de lire VERSION dans Main.lua."
    }
    $LuaVersion = $Matches[1]

    $Project = [xml](Get-Content -Raw $BridgeProject)
    $PropertyGroup = @($Project.Project.PropertyGroup) | Where-Object { $_.Version } | Select-Object -First 1
    if ($null -eq $PropertyGroup) {
        throw "Version .NET introuvable dans le csproj."
    }

    $ExpectedFileVersion = "$Version.0"
    $ExpectedInformationalVersion = "$Version-alpha"
    $VersionChecks = @(
        @{ Name = "VERSION"; Value = $Version; Expected = $Version },
        @{ Name = "manifest"; Value = $ManifestVersion; Expected = $Version },
        @{ Name = "Main.lua"; Value = $LuaVersion; Expected = $Version },
        @{ Name = "csproj Version"; Value = [string]$PropertyGroup.Version; Expected = $Version },
        @{ Name = "csproj AssemblyVersion"; Value = [string]$PropertyGroup.AssemblyVersion; Expected = $ExpectedFileVersion },
        @{ Name = "csproj FileVersion"; Value = [string]$PropertyGroup.FileVersion; Expected = $ExpectedFileVersion },
        @{ Name = "csproj InformationalVersion"; Value = [string]$PropertyGroup.InformationalVersion; Expected = $ExpectedInformationalVersion },
        @{ Name = "TargetFramework"; Value = [string]$PropertyGroup.TargetFramework; Expected = "net10.0" }
    )

    foreach ($Check in $VersionChecks) {
        if ($Check.Value -ne $Check.Expected) {
            throw "Incohérence $($Check.Name) : '$($Check.Value)' au lieu de '$($Check.Expected)'."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTag)) {
        $Pattern = '^v' + [regex]::Escape($Version) + '($|-)'
        if ($ExpectedTag -notmatch $Pattern) {
            throw "Le tag $ExpectedTag ne correspond pas à VERSION=$Version."
        }
    }

    Write-Host "Version source cohérente : $Version"

    dotnet restore $BridgeProject --runtime win-x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Restore bridge en échec." }

    dotnet restore $TestsProject --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Restore tests en échec." }

    $AuditOutput = & dotnet list $BridgeProject package --vulnerable --include-transitive --format json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Audit NuGet en échec : $($AuditOutput -join ' ')"
    }
    $AuditText = $AuditOutput -join "`n"
    if ($AuditText -match '"vulnerabilities"\s*:\s*\[\s*\{') {
        throw "Une dépendance NuGet vulnérable a été détectée."
    }
    Write-Host "Audit NuGet : aucune vulnérabilité connue détectée."

    dotnet build $TestsProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Compilation des tests en échec." }

    dotnet run --project $TestsProject --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests de régression en échec." }

    if (Test-Path $PublishDirectory) {
        Remove-Item $PublishDirectory -Recurse -Force
    }

    dotnet publish $BridgeProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        --output $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw "Publication win-x64 en échec." }

    $ExePath = Join-Path $PublishDirectory "LotroPresence.exe"
    $VersionInfo = (Get-Item $ExePath).VersionInfo
    if ($VersionInfo.FileVersion -ne $ExpectedFileVersion) {
        throw "FileVersion EXE invalide : $($VersionInfo.FileVersion), attendu $ExpectedFileVersion."
    }
    if ($VersionInfo.ProductVersion -notlike "$ExpectedInformationalVersion*") {
        throw "ProductVersion EXE invalide : $($VersionInfo.ProductVersion), attendu $ExpectedInformationalVersion."
    }

    $SelfTest = Start-Process -FilePath $ExePath -ArgumentList "--self-test" -Wait -PassThru
    if ($SelfTest.ExitCode -ne 0) {
        throw "Self-tests de l'exécutable en échec (code $($SelfTest.ExitCode))."
    }

    $Smoke = Start-Process -FilePath $ExePath -PassThru
    Start-Sleep -Seconds 2
    if ($Smoke.HasExited) {
        throw "LotroPresence s'est arrêté au démarrage silencieux (code $($Smoke.ExitCode))."
    }
    Stop-Process -Id $Smoke.Id -Force

    New-Item -ItemType Directory -Force -Path (Join-Path $PublishDirectory "plugin") | Out-Null
    Copy-Item -Path "plugin/Dusk" -Destination (Join-Path $PublishDirectory "plugin") -Recurse -Force
    Copy-Item -Path "README.md" -Destination (Join-Path $PublishDirectory "README.md") -Force
    Copy-Item -Path "NOTICE.md" -Destination (Join-Path $PublishDirectory "NOTICE.md") -Force
    Copy-Item -Path "VERSION" -Destination (Join-Path $PublishDirectory "VERSION") -Force

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    if (Test-Path $ChecksumPath) { Remove-Item $ChecksumPath -Force }

    Compress-Archive -Path (Join-Path $PublishDirectory "*") -DestinationPath $ZipPath -Force
    $Hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$Hash  LotroPresence-win-x64.zip" | Set-Content -Path $ChecksumPath -Encoding ascii

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $Archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $Entries = @($Archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $Required = @(
            "LotroPresence.exe",
            "config.default.json",
            "config.example.json",
            "plugin/Dusk/LotroPresence.plugin",
            "plugin/Dusk/LotroPresence/Main.lua",
            "README.md",
            "NOTICE.md",
            "VERSION"
        )
        foreach ($Entry in $Required) {
            if ($Entries -notcontains $Entry) {
                throw "Fichier manquant dans le ZIP : $Entry"
            }
        }
    }
    finally {
        $Archive.Dispose()
    }

    $ExpectedHash = ((Get-Content -Raw $ChecksumPath).Trim() -split '\s+')[0].ToLowerInvariant()
    $ActualHash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ActualHash -ne $ExpectedHash) {
        throw "Checksum invalide : attendu $ExpectedHash, obtenu $ActualHash."
    }

    Write-Host "Build validé. SHA-256 : $ActualHash"
}
finally {
    Pop-Location
}
