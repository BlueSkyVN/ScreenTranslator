# ========================================================================
# Script to automate packaging for submission (Source + Executable + Guide)
# ========================================================================
$ProjectDir = $PSScriptRoot
$TempDir = Join-Path $ProjectDir "Submission_Temp"
$ZipPath = Join-Path $ProjectDir "ScreenTranslator_v3.0_Submission.zip"

Write-Host "Cleaning up old files..." -ForegroundColor Yellow
if (Test-Path $TempDir) { Remove-Item -Recurse -Force $TempDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }

# Create temporary directory structure
New-Item -ItemType Directory -Path (Join-Path $TempDir "MaNguon") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $TempDir "ChuongTrinh") | Out-Null

# 1. Copy Installer to ChuongTrinh
$InstallerSrc = Join-Path $ProjectDir "Installer\Output\ScreenTranslatorPro_Setup_v3.0.exe"
if (Test-Path $InstallerSrc) {
    Write-Host "Copying installer program..." -ForegroundColor Yellow
    Copy-Item $InstallerSrc -Destination (Join-Path $TempDir "ChuongTrinh")
} else {
    Write-Error "Installer file not found at: $InstallerSrc. Please run Installer\build_fd_installer.ps1 first!"
    exit 1
}

# 2. Copy Installation guide
Write-Host "Copying installation guide..." -ForegroundColor Yellow
Copy-Item (Join-Path $ProjectDir "HuongDanCaiDat.txt") -Destination $TempDir

# 3. Copy Source files (exclude build artifacts, git, vs, temp folders, logs and scripts)
Write-Host "Filtering and copying source code..." -ForegroundColor Yellow
$Files = Get-ChildItem -Path $ProjectDir -Recurse -File
$CopyCount = 0

foreach ($File in $Files) {
    # Get relative path from project root
    $RelativePath = $File.FullName.Substring($ProjectDir.Length + 1)
    
    # Exclude pattern check (matches directories like bin, obj, .git, .vs anywhere in path)
    if ($RelativePath -match "(^|\\)(\.git|\.vs|bin|obj|Installer\\Output|Submission_Temp)($|\\)" -or
        $RelativePath -eq "ScreenTranslator_v3.0_Submission.zip" -or
        $RelativePath -eq "package_submission.ps1" -or
        $RelativePath -eq "Installer\build_fd_installer.ps1" -or
        $RelativePath -eq "Installer\build_installer.ps1" -or
        $RelativePath -match "^install_log\d?\.txt$") {
        continue
    }
    
    $DestFile = Join-Path $TempDir "MaNguon\$RelativePath"
    $DestFolder = Split-Path $DestFile -Parent
    
    # Create subfolders if they don't exist
    if (-not (Test-Path $DestFolder)) {
        New-Item -ItemType Directory -Path $DestFolder | Out-Null
    }
    
    Copy-Item $File.FullName -Destination $DestFile -Force
    $CopyCount++
}

Write-Host "Copied $CopyCount source files." -ForegroundColor Green

# 4. Zip the temporary directory
Write-Host "Creating ZIP file..." -ForegroundColor Yellow
Compress-Archive -Path "$TempDir\*" -DestinationPath $ZipPath -Force

# 5. Clean up temporary directory
Write-Host "Cleaning up temporary directory..." -ForegroundColor Yellow
Remove-Item -Recurse -Force $TempDir

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  PACKAGING SUCCESSFUL!" -ForegroundColor Green
Write-Host "  Submission File: $ZipPath" -ForegroundColor Green
Write-Host "  File Size: $([Math]::Round(((Get-Item $ZipPath).Length / 1MB), 2)) MB" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
