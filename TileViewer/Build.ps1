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
# /restore fetches the ImGui.NET package (PackageReference) before building.
$arguments = @((Join-Path $PSScriptRoot 'TileViewer.csproj'), '/restore', '/t:Build', "/p:Configuration=$Configuration", "/p:CSpectDirectory=$CSpectDirectory", '/v:minimal', '/nologo')
$referencePack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
if (!(Test-Path -LiteralPath $referencePack)) {
    # The installed .NET Framework 4.8 runtime assemblies also allow a local
    # build without installing a targeting pack.
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
    # The tests drive the viewer model and a fake window, so no ImGui native library is needed,
    # but the view still compiles against ImGui.NET from the build output.
    $output = Join-Path $PSScriptRoot "bin\$Configuration"
    $imgui = Join-Path $output 'ImGui.NET.dll'
    $vectors = Join-Path $output 'System.Numerics.Vectors.dll'
    $references = @("/reference:$api", "/reference:$imgui", '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Numerics.dll')
    if (Test-Path -LiteralPath $vectors) { $references += "/reference:$vectors" }
    $sources = 'TileSnapshot.cs', 'TileMapEditor.cs', 'TileViewerPlugin.cs', 'TileViewerModel.cs', 'TileViewerView.cs', 'Tests\TileViewerTests.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & $compiler /nologo /target:exe "/out:$testExe" @references @sources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath $api -Destination $testDirectory
    Get-ChildItem -LiteralPath $output -Filter *.dll | Copy-Item -Destination $testDirectory
    & $testExe $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'TileViewer tests failed.' }
}
