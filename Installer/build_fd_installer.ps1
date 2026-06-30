# ============================================
# Script build & package Installer (Framework Dependent)
# ============================================
$Configuration = "Release"
$Runtime = "win-x64"
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "..\ScreenTranslator.csproj"

Write-Host "Publishing framework-dependent release..." -ForegroundColor Yellow
dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained false -o "$ProjectDir\..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

Write-Host "Looking for Inno Setup..." -ForegroundColor Yellow
$InnoPath = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
    "C:\Users\PC\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $InnoPath) {
    Write-Host "Inno Setup 6 not found! Creating ZIP instead..." -ForegroundColor Yellow
    $PublishDir = "$ProjectDir\..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
    $ZipOutput = "$ProjectDir\Output\ScreenTranslatorPro_v3.0_Portable.zip"
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipOutput -Force
    Write-Host "ZIP created: $ZipOutput" -ForegroundColor Green
} else {
    Write-Host "Found Inno Setup at: $InnoPath" -ForegroundColor Green
    Push-Location (Join-Path $ProjectDir "..")
    & $InnoPath "$ProjectDir\setup.iss"
    Pop-Location
    Write-Host "Installer built successfully!" -ForegroundColor Green
}
