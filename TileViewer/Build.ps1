param(
    [Parameter(Mandatory = $true)][string]$CSpectDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$api = Join-Path $CSpectDirectory 'Plugin.dll'
if (!(Test-Path -LiteralPath $api)) { throw "CSpect Plugin.dll not found: $api" }
$CSpectDirectory = (Resolve-Path -LiteralPath $CSpectDirectory).ProviderPath
$api = Join-Path $CSpectDirectory 'Plugin.dll'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = $null
if (Test-Path -LiteralPath $vswhere) {
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (!$msbuild) {
    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($command) { $msbuild = $command.Source }
}
if (!$msbuild) { throw 'MSBuild is required (Visual Studio Build Tools is sufficient).' }
$arguments = @((Join-Path $PSScriptRoot 'TileViewer.csproj'), '/t:Build', "/p:Configuration=$Configuration", "/p:CSpectDirectory=$CSpectDirectory", '/v:minimal', '/nologo')
$referencePack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.2\mscorlib.dll'
if (!(Test-Path -LiteralPath $referencePack)) {
    # This plugin uses framework APIs available in 4.5.2; the installed CLR 4
    # assemblies also allow a local build without installing a targeting pack.
    $runtime = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    $arguments += "/p:FrameworkPathOverride=$runtime", '/p:BypassFrameworkInstallChecks=true'
}
& $msbuild @arguments
if ($LASTEXITCODE -ne 0) { throw 'TileViewer build failed.' }

if ($Test) {
    $compiler = Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe'
    if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    $testDirectory = Join-Path $PSScriptRoot 'obj\Tests'
    New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
    $testExe = Join-Path $testDirectory 'TileViewer.Tests.exe'
    & $compiler /nologo /target:exe "/out:$testExe" "/reference:$api" /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'TileSnapshot.cs') (Join-Path $PSScriptRoot 'TileMapEditor.cs') (Join-Path $PSScriptRoot 'TileViewerPlugin.cs') (Join-Path $PSScriptRoot 'TileViewerForm.cs') (Join-Path $PSScriptRoot 'Tests\TileViewerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath $api -Destination $testDirectory
    & $testExe $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'TileViewer tests failed.' }
}
