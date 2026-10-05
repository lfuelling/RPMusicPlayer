param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$TypeName,
    [Parameter(Mandatory = $true)][string]$MethodName
)

$kspManaged = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\KSP_x64_Data\Managed"
Add-Type -Path (Join-Path $kspManaged "Mono.Cecil.dll")

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Assembly)

foreach ($t in $asm.MainModule.GetTypes()) {
    if ($t.Name -ne $TypeName) { continue }
    foreach ($m in $t.Methods) {
        if ($m.Name -ne $MethodName) { continue }
        Write-Output ("=== {0}.{1} ===" -f $t.Name, $m.Name)
        if (-not $m.HasBody) { Write-Output "  (no body)"; continue }
        foreach ($ins in $m.Body.Instructions) {
            $operand = $ins.Operand
            if ($operand -is [Mono.Cecil.MethodReference]) {
                $operand = "{0}::{1}" -f $operand.DeclaringType.Name, $operand.Name
            } elseif ($operand -is [Mono.Cecil.FieldReference]) {
                $operand = "{0}::{1}" -f $operand.DeclaringType.Name, $operand.Name
            }
            Write-Output ("  {0,-12} {1}" -f $ins.OpCode.Name, $operand)
        }
    }
}