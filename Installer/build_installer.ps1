# ============================================
# Script tự động Build và đóng gói Installer
# ============================================
# Cách dùng: Chạy script này trong PowerShell tại thư mục gốc dự án
# Yêu cầu: Inno Setup 6 đã được cài đặt

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "..\ScreenTranslator.csproj"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Screen Translator Pro - Build & Package Script" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

# Step 1: Build Release
Write-Host "`n[1/3] Building project ($Configuration | $Runtime)..." -ForegroundColor Yellow
dotnet publish $ProjectFile -c $Configuration -r $Runtime --self-contained -o "$ProjectDir\..\bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime\publish"

if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

# Step 2: Tìm Inno Setup Compiler
Write-Host "`n[2/3] Looking for Inno Setup Compiler..." -ForegroundColor Yellow
$InnoPath = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
    "C:\Users\PC\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $InnoPath) {
    Write-Host "Inno Setup 6 not found! Please install from https://jrsoftware.org/isdl.php" -ForegroundColor Red
    Write-Host "Skipping installer creation. You can still run the app from the publish folder." -ForegroundColor Yellow
    
    # Tạo portable ZIP thay thế
    Write-Host "`n[3/3] Creating portable ZIP package..." -ForegroundColor Yellow
    $PublishDir = Join-Path $ProjectDir "..\bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime\publish"
    $ZipOutput = Join-Path $ProjectDir "Output\ScreenTranslatorPro_v3.0_Portable.zip"
    
    $OutputDir = Join-Path $ProjectDir "Output"
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }
    
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipOutput -Force
    Write-Host "Portable ZIP created: $ZipOutput" -ForegroundColor Green
    exit 0
}

Write-Host "Found: $InnoPath" -ForegroundColor Green

# Step 3: Build Installer
Write-Host "`n[3/3] Building installer..." -ForegroundColor Yellow
$IssFile = Join-Path $ProjectDir "setup.iss"

# Đổi working directory để Inno Setup tìm được file paths
Push-Location (Join-Path $ProjectDir "..")
& $InnoPath $IssFile
Pop-Location

if ($LASTEXITCODE -ne 0) {
    Write-Host "INSTALLER BUILD FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host "`n================================================" -ForegroundColor Green
Write-Host "  BUILD & PACKAGE COMPLETE!" -ForegroundColor Green
Write-Host "  Installer: Installer\Output\" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
