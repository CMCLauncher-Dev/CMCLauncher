$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('cmcl-package-check-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $fixture 'publish'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
$files = @(
    'CMCLauncher.exe', 'README.md', 'LICENSE', 'locales/en_us.json', 'themes/custom.json',
    'assets/logo.png', 'logs/session.log', 'frpc/configs/private.toml', 'cmcl/frpc/secret.ini',
    '.minecraft/saves/world/level.dat', 'state/accounts.json', 'instances/world.json',
    'tools/frpc.exe', 'crash-reports/crash.txt', 'cache/session.json', 'settings.json',
    'curseforge.local.json', 'private.key', 'another.log', '.cmcl-write-test'
)
foreach ($file in $files)
{
    $path = Join-Path $publish $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'fixture')
}
$outside = Join-Path $fixture 'outside'
New-Item -ItemType Directory -Path $outside -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $outside 'private.txt'), 'must not be distributed')
New-Item -ItemType Junction -Path (Join-Path $publish 'linked-folder') -Target $outside | Out-Null
$archive = Join-Path $fixture 'package.zip'
& (Join-Path $PSScriptRoot 'package-release.ps1') -PublishDirectory $publish -ArchivePath $archive
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try
{
    $names = @($zip.Entries | ForEach-Object FullName)
    $expected = @('CMCLauncher.exe', 'README.md', 'LICENSE', 'locales/en_us.json', 'themes/custom.json', 'assets/logo.png')
    if ($names.Count -ne $expected.Count -or @($names | Where-Object { $_ -notin $expected }).Count -gt 0)
    {
        throw "Unexpected packaged files: $($names -join ', ')"
    }
    foreach ($name in $expected)
    {
        if (@($names | Where-Object { $_ -eq $name }).Count -ne 1) { throw "Missing or duplicate entry: $name" }
    }
}
finally { $zip.Dispose() }
Write-Output 'PASS Release packaging excludes runtime data and includes documentation exactly once.'
