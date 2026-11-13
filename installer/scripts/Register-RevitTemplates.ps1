<#
  Register / unregister DHBIMWATER project templates in Revit.ini (docs/324).
  Revit API cannot edit the template list, so the DefaultTemplate key is edited directly.
  - Sections: [DirectoriesKOR], [DirectoriesENU]. Missing sections are skipped (never created).
  - Target ini: user ini, else machine UserDataCache ini. Never creates an ini file.
  - Entry name = .rte file name without extension. Same name already listed -> replaced.
  - File stays UTF-16 LE with BOM. Revit must be closed (Revit rewrites ini on exit).
  Keep this file ASCII only (Windows PowerShell 5.1 reads BOM-less files as ANSI).
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('Register', 'Unregister')][string]$Action,
    [Parameter(Mandatory = $true)][string]$RevitVersion,
    [Parameter(Mandatory = $true)][string]$TemplateDir,
    # Test only: skip ini lookup and edit this file
    [string]$IniPath
)

$ErrorActionPreference = 'Stop'

try {
    if (-not $IniPath) {
        $userIni = Join-Path $env:APPDATA "Autodesk\Revit\Autodesk Revit $RevitVersion\Revit.ini"
        $machineIni = "C:\ProgramData\Autodesk\RVT $RevitVersion\UserDataCache\Revit.ini"
        if (Test-Path -LiteralPath $userIni) { $IniPath = $userIni }
        elseif (Test-Path -LiteralPath $machineIni) { $IniPath = $machineIni }
        else {
            Write-Output "Revit $RevitVersion ini not found - skipped"
            exit 0
        }
    }

    $templateRoot = [IO.Path]::GetFullPath($TemplateDir).TrimEnd('\') + '\'
    # Revit relative paths (..\..\..\ProgramData\...) are based on the Revit install folder
    $programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $relativeBase = Join-Path $programFiles "Autodesk\Revit $RevitVersion"

    # New entries (Register only)
    $newEntries = New-Object System.Collections.Generic.List[string]
    $newNames = New-Object System.Collections.Generic.List[string]
    if ($Action -eq 'Register' -and (Test-Path -LiteralPath $TemplateDir)) {
        $files = Get-ChildItem -LiteralPath $TemplateDir -File |
            Where-Object { $_.Extension -ieq '.rte' } | Sort-Object Name
        foreach ($file in $files) {
            $name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
            if ($name -match '[,=]') {
                Write-Output "WARN: '$($file.Name)' has ',' or '=' in name - skipped"
                continue
            }
            $newEntries.Add("$name=$($file.FullName)")
            $newNames.Add($name)
        }
    }

    function Test-UnderTemplateRoot([string]$path) {
        try {
            $full = if ([IO.Path]::IsPathRooted($path)) { [IO.Path]::GetFullPath($path) }
                    else { [IO.Path]::GetFullPath((Join-Path $relativeBase $path)) }
            return $full.StartsWith($templateRoot, [StringComparison]::OrdinalIgnoreCase)
        } catch { return $false }
    }

    function Get-NewValue([string]$value) {
        $result = New-Object System.Collections.Generic.List[string]
        foreach ($e in $newEntries) { $result.Add($e) }
        foreach ($raw in ($value -split ',')) {
            $item = $raw.Trim()
            if ($item.Length -eq 0) { continue }
            $eq = $item.IndexOf('=')
            if ($eq -gt 0) {
                $name = $item.Substring(0, $eq).Trim()
                $path = $item.Substring($eq + 1).Trim()
                # our folder entries: always drop (re-added above on Register)
                if (Test-UnderTemplateRoot $path) { continue }
                # same display name: replace with new path
                if ($newNames -contains $name) { continue }
            }
            $result.Add($item)
        }
        return ($result -join ', ')
    }

    $text = [IO.File]::ReadAllText($IniPath, [Text.Encoding]::Unicode)
    $lines = $text -split "`r`n"
    $targetSections = @('[DirectoriesKOR]', '[DirectoriesENU]')

    $out = New-Object System.Collections.Generic.List[string]
    $headerIndex = -1      # index in $out of current target section header
    $foundKey = $false
    $changed = $false

    # close current target section: add key if missing (Register only)
    $closeSection = {
        if ($headerIndex -ge 0 -and -not $foundKey -and $newEntries.Count -gt 0) {
            $out.Insert($headerIndex + 1, 'DefaultTemplate=' + ($newEntries -join ', '))
            $script:changed = $true
        }
    }

    foreach ($line in $lines) {
        $trim = $line.Trim()
        if ($trim.StartsWith('[') -and $trim.EndsWith(']')) {
            & $closeSection
            $headerIndex = -1
            $foundKey = $false
            if ($targetSections -contains $trim) { $headerIndex = $out.Count }
            $out.Add($line)
            continue
        }
        if ($headerIndex -ge 0 -and $line.StartsWith('DefaultTemplate=')) {
            $foundKey = $true
            $oldValue = $line.Substring('DefaultTemplate='.Length)
            $newValue = Get-NewValue $oldValue
            if ($newValue -ne $oldValue) { $changed = $true }
            $out.Add('DefaultTemplate=' + $newValue)
            continue
        }
        $out.Add($line)
    }
    & $closeSection

    if (-not $changed) {
        Write-Output "$IniPath : no change"
        exit 0
    }

    Copy-Item -LiteralPath $IniPath -Destination ($IniPath + '.dhbak') -Force
    [IO.File]::WriteAllText($IniPath, ($out -join "`r`n"), (New-Object System.Text.UnicodeEncoding($false, $true)))
    Write-Output "$IniPath : $Action done ($($newEntries.Count) template(s))"
    exit 0
}
catch {
    Write-Output "ERROR: $($_.Exception.Message)"
    exit 1
}
