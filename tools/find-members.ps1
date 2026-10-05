param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$Pattern
)

$kspManaged = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed"
Add-Type -Path (Join-Path $kspManaged "Mono.Cecil.dll")

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