param([switch]$Test, [switch]$Publish, [string]$GameExe = 'E:\steam\steamapps\common\Ostriv\ostriv.exe')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore TranslateOstriv.slnx --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build TranslateOstriv.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ($Test) {
        dotnet run --project tests/OstrivTranslate.Tests -c Release --no-build -- $GameExe
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    }
    if ($Publish) {
        dotnet publish src/OstrivTranslate.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/release
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Copy-Item -LiteralPath README.md, THIRD-PARTY-NOTICES.txt -Destination artifacts/release
        New-Item -ItemType Directory -Path artifacts/release/docs -Force | Out-Null
        Copy-Item -LiteralPath docs/VALIDATION.md -Destination artifacts/release/docs
        $packageDirectory = (dotnet nuget locals global-packages --list --force-english-output) -replace '^global-packages:\s*', ''
        if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the runtime license packages' }
        $runtimeConfig = Get-Content 'src/OstrivTranslate.App/bin/Release/net10.0-windows/win-x64/OstrivTranslate.runtimeconfig.json' -Raw | ConvertFrom-Json
        New-Item -ItemType Directory -Path artifacts/release/licenses -Force | Out-Null
        foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
            $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
            $packagePath = Join-Path $packageDirectory (Join-Path $packageName $framework.version)
            foreach ($licenseFile in (Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'LICENSE|THIRD-PARTY-NOTICES' })) {
                Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path 'artifacts/release/licenses' ($packageName + '-' + $licenseFile.Name + '.txt'))
            }
        }
        Write-Output 'Portable application: artifacts/release/OstrivTranslate.exe'
    }
}
finally { Pop-Location }
