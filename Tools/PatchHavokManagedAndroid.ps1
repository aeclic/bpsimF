param(
    [Parameter(Mandatory = $true)] [string] $UnityEditorDirectory
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $project 'Assets/Plugins/Havok.Physics.dll'
$native = Join-Path $project 'Assets/Plugins/Android/libs/armeabi-v7a/libHavokNative.so'
$output = Join-Path $project 'Assets/Plugins/Android/Havok.Physics.dll'

if ((Get-FileHash -LiteralPath $source).Hash -ne 'E138E4FD284C165C066438EF85FE347932101DE98152EFB713F4FE167F089757' -or
    (Get-FileHash -LiteralPath $native).Hash -ne '3BC6CA343CCC366391F332B703C0F8B4F6AA14F185E21376D59D908BEAC96C04') {
    throw 'The Havok binaries changed. Recheck the ARMv7 layouts before regenerating the managed plugin.'
}

Add-Type -Path (Join-Path $UnityEditorDirectory 'Data/Managed/Unity.Cecil.dll')
$assembly = [Mono.Cecil.ModuleDefinition]::ReadModule($source)
try {
    foreach ($layout in @(
        @{ Name = 'HpBlockStreamRange'; Desktop = 16; Arm = 12 },
        @{ Name = 'HpLinkedRange'; Desktop = 32; Arm = 16 },
        @{ Name = 'HpCsContactJacRange'; Desktop = 48; Arm = 32 }
    )) {
        $type = $assembly.GetType('Havok.Physics.' + $layout.Name)
        if ($null -eq $type -or -not $type.IsSequentialLayout -or $type.ClassSize -ne $layout.Desktop) {
            throw "Unexpected managed layout: $($layout.Name)"
        }
        $type.ClassSize = $layout.Arm
    }
    $assembly.Mvid = [guid]'4d70bd89-b767-4d63-88a0-4837ff0b6fbf'
    $assembly.Write($output)
}
finally {
    $assembly.Dispose()
}

Write-Output $output
