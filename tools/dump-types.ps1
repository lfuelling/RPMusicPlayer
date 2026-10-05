<#
.SYNOPSIS
    Lists the public types and members of a KSP or RasterPropMonitor assembly.

.DESCRIPTION
    Dumps signatures only, which is what you want when working out which API is
    actually available to call. For the instructions inside a single method, see
    find-members.ps1 and dump-il.ps1.

.PARAMETER Assembly
    Path to the assembly to read, for example KSP_x64_Data\Managed\Assembly-CSharp.dll.

.PARAMETER Filter
    Optional regular expression; only types whose full name matches are listed.

.PARAMETER KspDir
    KSP install, used to find Mono.Cecil.dll. Defaults to $env:KSP_DIR, then the
    default Steam install.

.EXAMPLE
    ./dump-types.ps1 -Assembly ..\..\KSP_x64_Data\Managed\Assembly-CSharp.dll -Filter Camera
#>
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [string]$Filter = "",
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