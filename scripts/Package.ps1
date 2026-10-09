param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$UpdateUrl = ''
)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $workspaceRoot
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot '.build/dotnet'
$env:NUGET_PACKAGES = Join-Path $workspaceRoot '.build/packages'
if ($UpdateUrl) {
    $feedUri = [Uri]$UpdateUrl
    if (-not $feedUri.IsAbsoluteUri -or $feedUri.Scheme -ne 'https' -or $feedUri.UserInfo) { throw 'UpdateUrl must be HTTPS without embedded credentials.' }
}
$publishPath = Join-Path $workspaceRoot "artifacts/package-$Version"
$releasePath = Join-Path $workspaceRoot 'artifacts/releases'
$vpkPath = Join-Path $workspaceRoot '.build/tools/vpk.exe'
if (-not (Test-Path -LiteralPath $vpkPath)) {
    dotnet tool install vpk --version 1.2.0 --tool-path .build/tools
    if ($LASTEXITCODE -ne 0) { throw 'Velopack tool installation failed.' }
}
dotnet publish src/Hushtype.App/Hushtype.App.csproj -c Release -r win-x64 --self-contained true -o $publishPath -p:Version=$Version -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
[System.IO.File]::WriteAllText((Join-Path $publishPath 'update-source.json'), (@{ Url = $UpdateUrl } | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath MODEL_NOTICE.md,README.md -Destination $publishPath
& $vpkPath pack --packId Hushtype --packVersion $Version --packDir $publishPath --mainExe Hushtype.App.exe --packTitle Hushtype --packAuthors Hushtype --icon assets/hushtype.ico --outputDir $releasePath --framework vcredist143-x64 --shortcuts StartMenuRoot
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
Get-ChildItem -LiteralPath $releasePath -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    Write-Output "$($hash.Hash)  $($_.Name)"
} | Set-Content -LiteralPath (Join-Path $releasePath 'SHA256SUMS.txt') -Encoding UTF8
Write-Output "Release artifacts: $releasePath"
