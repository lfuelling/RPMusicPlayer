param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [string]$Filter = ""
)

$kspManaged = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed"
Add-Type -Path (Join-Path $kspManaged "Mono.Cecil.dll")

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Assembly)

foreach ($t in $asm.MainModule.GetTypes()) {
    if ($Filter -ne "" -and $t.FullName -notmatch $Filter) { continue }
    Write-Output ("TYPE  {0} : {1}  [{2}]" -f $t.FullName, $t.BaseType, $t.Attributes)
    foreach ($m in $t.Methods) {
        $params = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Output ("    M   {0} {1}({2})" -f $m.ReturnType.Name, $m.Name, $params)
    }
    foreach ($f in $t.Fields) {
        Write-Output ("    F   {0} {1}" -f $f.FieldType.Name, $f.Name)
    }
}