param([switch]$RunChecks)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$binDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $binDir -Force | Out-Null
$output = Join-Path $binDir 'GpuDesktopKeeper.exe'
& (Join-Path $PSScriptRoot 'BuildIcon.ps1')
$sources = @('GpuDevice.cs', 'KeeperEngine.cs', 'Program.cs', 'KeeperWindow.cs', 'Checks.cs','Startup.cs','TaskStartup.cs','AppIcons.cs','FlydigiNotice.cs','Theme.cs','UiText.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:x86 /optimize+ /warn:4 /codepage:65001 "/out:$output" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\Keeper.ico" "/resource:$PSScriptRoot\Keeper.ico,Keeper.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll @sources
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GpuDesktopKeeper.exe.config') -Destination $binDir -Force
if ($RunChecks) {
    $check = Start-Process -FilePath $output -ArgumentList '--check-core' -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "Core checks failed; see bin\core-check-result.json" }
}
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Format-List
