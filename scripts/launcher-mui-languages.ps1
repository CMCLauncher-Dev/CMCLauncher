# Keep native WinUI resources aligned with the interface packs shipped by the launcher.
function Get-LauncherMuiLanguages
{
    param([string]$LocaleDirectory = (Join-Path $PSScriptRoot '../CMCLauncher.Core/Resources/Locales'))
    $cultures = @('en-US')
    if ($PSVersionTable.PSVersion.Major -lt 6)
    {
        # Locale keys can differ only by case. Windows PowerShell's
        # ConvertFrom-Json cannot represent them as a PSObject.
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    }
    foreach ($file in Get-ChildItem -LiteralPath $LocaleDirectory -Filter '*.json' -File)
    {
        if ($file.BaseName -notmatch '^[a-z]{2}_[a-z]{2}$')
        {
            continue
        }
        $json = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        $pack = if ($PSVersionTable.PSVersion.Major -lt 6)
        {
            $serializer.DeserializeObject($json)
        }
        else
        {
            $json | ConvertFrom-Json -AsHashtable
        }
        if ($pack["code"] -ne $file.BaseName)
        {
            throw "Language pack code does not match its filename: $($file.Name)"
        }
        $cultures += [Globalization.CultureInfo]::GetCultureInfo($file.BaseName.Replace('_', '-')).Name
    }
    return @($cultures | Sort-Object -Unique)
}
