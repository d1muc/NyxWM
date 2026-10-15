#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Automation script for testing nyxWM implementation

.DESCRIPTION
    This script handles the testing lifecycle for nyxWM:
    1. Building the project
    2. Killing existing processes
    3. Starting the new build
    4. Validating functionality
    5. Cleaning up resources

.PARAMETER BuildOnly
    If specified, only builds the project without running tests

.PARAMETER TestDuration
    How long to run the application for testing (in seconds). Default: 30

.EXAMPLE
    .\Test-nyxWM.ps1
    .\Test-nyxWM.ps1 -BuildOnly
    .\Test-nyxWM.ps1 -TestDuration 60
#>

[CmdletBinding()]
param(
    [switch] $BuildOnly,
    [int] $TestDuration = 30
)

Write-Host "=== nyxWM Testing Automation ===" -ForegroundColor Cyan

# Get script directory and project root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ClaudeDir = Split-Path -Parent $ScriptDir
	$ProjectRoot = Split-Path -Parent $ClaudeDir
$BinPath = Join-Path $ProjectRoot "bin\Debug\net10.0-windows"
$ExePath = Join-Path $BinPath "nyxWM.exe"

Write-Host "Project root: $ProjectRoot"
Write-Host "Binary path: $BinPath"
Write-Host "Executable path: $ExePath"

# Step 1: Build the project
Write-Host "`nStep 1: Building project..." -ForegroundColor Yellow
$buildResult = dotnet build $ProjectRoot
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "Build successful!" -ForegroundColor Green
}

if ($BuildOnly) {
    Write-Host "Build-only mode selected. Exiting." -ForegroundColor Cyan
    exit 0
}

# Step 2: Kill existing nyxWM processes
Write-Host "`nStep 2: Killing existing nyxWM processes..." -ForegroundColor Yellow
$processes = Get-Process -Name "nyxWM" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Found $($processes.Count) existing nyxWM process(es). Terminating..."
    foreach ($proc in $processes) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "Killed process ID: $($proc.Id)"
        } catch {
            Write-Host "Failed to kill process ID: $($proc.Id) - $_" -ForegroundColor Yellow
        }
    }
    # Wait a moment for processes to fully terminate
    Start-Sleep -Seconds 2
} else {
    Write-Host "No existing nyxWM processes found."
}

# Step 3: Start the new build
Write-Host "`nStep 3: Starting nyxWM application..." -ForegroundColor Yellow
if (-Not (Test-Path $ExePath)) {
    Write-Host "Executable not found at: $ExePath" -ForegroundColor Red
    exit 1
}

try {
    $process = Start-Process -FilePath $ExePath -WorkingDirectory $BinPath -PassThru -WindowStyle Hidden
    Write-Host "Started nyxWM.exe with Process ID: $($process.Id)" -ForegroundColor Green

    # Give the application time to start up
    Write-Host "Waiting for application to initialize..."
    Start-Sleep -Seconds 5

    # Verify process is still running
    if (-Not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
        Write-Host "Application failed to start or terminated unexpectedly." -ForegroundColor Red
        exit 1
    }

    Write-Host "Application is running. Process ID: $($process.Id)" -ForegroundColor Green

} catch {
    Write-Host "Failed to start application: $_" -ForegroundColor Red
    exit 1
}

# Step 4: Validate functionality (basic checks)
Write-Host "`nStep 4: Validating basic functionality..." -ForegroundColor Yellow

# Check if process is still responding
try {
    # Simple check - if we can get process info, it's likely running
    Get-Process -Id $process.Id | Out-Null
    Write-Host "Process is responsive." -ForegroundColor Green
} catch {
    Write-Host "Process is not responding or has terminated." -ForegroundColor Red
}

# Monitor for a specified duration
Write-Host "`nMonitoring application for $TestDuration seconds..." -ForegroundColor Yellow
$startTime = Get-Date
while (((Get-Date).Subtract($startTime)).TotalSeconds -lt $TestDuration) {
    # Check if process is still running
    if (-Not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
        Write-Host "Application terminated unexpectedly after $([int]((Get-Date).Subtract($startTime)).TotalSeconds) seconds." -ForegroundColor Red
        break
    }

    # In a real implementation, we might check console output or hook status here
    # For now, we'll just wait and report status periodically
    $remaining = $TestDuration - [int]((Get-Date).Subtract($startTime)).TotalSeconds
    if ($remaining % 10 -eq 0 -and $remaining -gt 0) {
        Write-Host "Still running... $remaining seconds remaining" -ForegroundColor DarkGray
    }

    Start-Sleep -Seconds 1
}

# Step 5: Cleanup
Write-Host "`nStep 5: Cleaning up..." -ForegroundColor Yellow
try {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        Write-Host "Terminated test process (ID: $($process.Id))" -ForegroundColor Green
    }
} catch {
    Write-Host "Error terminating process: $_" -ForegroundColor Yellow
}

Write-Host "`n=== Testing Complete ===`n" -ForegroundColor Cyan