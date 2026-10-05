<#
.SYNOPSIS
    Disassembles one method of an assembly as IL.

.DESCRIPTION
    Last resort when a signature is not enough to tell what a method does, for
    example when working out how RasterPropMonitor picks the button for a page.

.PARAMETER Assembly
    Path to the assembly to read.

.PARAMETER TypeName
    Name of the type declaring the method.

.PARAMETER MethodName
    Name of the method to disassemble.

.PARAMETER KspDir
    KSP install, used to find Mono.Cecil.dll. Defaults to $env:KSP_DIR, then the
    default Steam install.

.EXAMPLE
    ./dump-il.ps1 -Assembly .\RasterPropMonitor.dll -TypeName RasterPropMonitor -MethodName PageButtonClick
#>
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$TypeName,
    [Parameter(Mandatory = $true)][string]$MethodName,
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