<#
.SYNOPSIS
    Finds the members of an assembly whose name matches a pattern.

.DESCRIPTION
    Useful when you know what something is called but not where it lives: it lists
    every method, property and field whose name matches, with its declaring type.

.PARAMETER Assembly
    Path to the assembly to search.

.PARAMETER Pattern
    Regular expression matched against member names.

.PARAMETER KspDir
    KSP install, used to find Mono.Cecil.dll. Defaults to $env:KSP_DIR, then the
    default Steam install.

.EXAMPLE
    ./find-members.ps1 -Assembly .\RasterPropMonitor.dll -Pattern "Button"
#>
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$Pattern,
    [string]$KspDir = $env:KSP_DIR
)

if ([string]::IsNullOrWhiteSpace($KspDir)) {
    $KspDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
}

$cecil = Join-Path $KspDir 'KSP_x64_Data\Managed\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecil)) {
    throw "Mono.Cecil.dll not found at $cecil. Pass -KspDir, or set the KSP_DIR environment variable."
}

Add-Type -Path $cecil

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Assembly)
$regex = New-Object System.Text.RegularExpressions.Regex($Pattern)

foreach ($t in $asm.MainModule.GetTypes()) {
    if (-not $t.IsPublic -and $t.IsNestedPrivate) { continue }
    foreach ($m in $t.Methods) {
        if ($regex.IsMatch($m.Name)) {
            Write-Output ("{0}.{1} {2}" -f $t.Name, $m.Name, $m.ReturnType.Name)
        }
    }
    foreach ($p in $t.Properties) {
        if ($regex.IsMatch($p.Name)) {
            Write-Output ("{0}.{1} (property) {2}" -f $t.Name, $p.Name, $p.PropertyType.Name)
        }
    }
    foreach ($f in $t.Fields) {
        if ($regex.IsMatch($f.Name)) {
            Write-Output ("{0}.{1} (field) {2}" -f $t.Name, $f.Name, $f.FieldType.Name)
        }
    }
}