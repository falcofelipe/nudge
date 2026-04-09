# Nudge - Publish Script
# Creates a standalone Nudge.exe that can be run by double-clicking.
#
# Usage:
#   .\publish.ps1
#   .\publish.ps1 -OutputDir "C:\MyApps\Nudge"
#
# After publishing, copy the entire output folder wherever you like and
# double-click Nudge.exe to start. A config/ folder will be created next
# to the .exe on first run.

param(
    [string]$OutputDir = ".\publish"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing Nudge..." -ForegroundColor Cyan

# Clean previous publish output
if (Test-Path $OutputDir) {
    Write-Host "Cleaning previous publish output..."
    Remove-Item -Recurse -Force $OutputDir
}

# Publish as a self-contained single-file executable
dotnet publish src/Nudge -c Release -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Copy the config folder next to the .exe so it works out of the box
$configSource = ".\config"
$configDest = Join-Path $OutputDir "config"
if (Test-Path $configSource) {
    Write-Host "Copying config folder..."
    Copy-Item -Recurse -Force $configSource $configDest
}

Write-Host ""
Write-Host "Published successfully!" -ForegroundColor Green
Write-Host "Output: $((Resolve-Path $OutputDir).Path)" -ForegroundColor Yellow
Write-Host ""
Write-Host "You can now:" -ForegroundColor Cyan
Write-Host "  1. Double-click Nudge.exe to launch"
Write-Host "  2. Move the entire '$OutputDir' folder anywhere you like"
Write-Host "  3. Create a shortcut to Nudge.exe on your Desktop or Start Menu"
Write-Host "  4. (Optional) Add a shortcut to your Startup folder to auto-start with Windows:"
Write-Host "     shell:startup" -ForegroundColor DarkGray
Write-Host ""
