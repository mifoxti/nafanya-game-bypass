param([string]$OutputPath, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}
$source = Join-Path $PSScriptRoot 'src'
$target = if ($OutputPath) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    Join-Path $PSScriptRoot 'bin\NafanyaGameBypass.exe'
}
$null = New-Item -Path ([IO.Path]::GetDirectoryName($target)) -ItemType Directory -Force
$sources = @(
    'AssemblyInfo.cs', 'Models.cs', 'RuleManager.cs', 'GlassUi.cs',
    'MainWindow.cs', 'ProcessPicker.cs', 'Program.cs'
) | ForEach-Object { Join-Path $source $_ }
$wpf = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') |
    ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$resources = @(
    'GlassTheme.xaml', 'MainWindow.xaml', 'ProcessPicker.xaml', 'DetailsWindow.xaml'
) | ForEach-Object {
    '/resource:' + (Join-Path $source $_) + ',NafanyaGameBypass.' + $_
}
$manifest = Join-Path $source 'app.manifest'
if ($Preview) {
    $manifestText = (Get-Content -Raw -LiteralPath $manifest).Replace('requireAdministrator', 'asInvoker')
    $manifest = Join-Path ([IO.Path]::GetDirectoryName($target)) 'glass-preview.manifest'
    $manifestText | Set-Content -LiteralPath $manifest -Encoding utf8
}
& $compiler /nologo /target:winexe /platform:anycpu /langversion:5 /optimize+ /utf8output `
    "/out:$target" "/win32manifest:$manifest" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll /reference:System.Xaml.dll `
    @references @resources @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output $target
