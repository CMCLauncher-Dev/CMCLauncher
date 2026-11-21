# The OAuth client ID is public and is embedded in the desktop application.
param([Parameter(Mandatory = $true)][string]$OutputPath)

$ErrorActionPreference = "Stop"
$id = [guid]::Empty
if (![guid]::TryParse($env:MICROSOFT_CLIENT_ID, [ref]$id) -or $id -eq [guid]::Empty)
{
    throw "Set MICROSOFT_CLIENT_ID to a valid Microsoft application client ID before publishing."
}
$json = @{ clientId = $id.ToString("D") } | ConvertTo-Json -Compress
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
