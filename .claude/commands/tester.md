# Tester Agent (/tester)

Verifies implementation by building the project, killing any existing nyxWM.exe process, starting the new build, and validating system behavior including WinAPI hooks functionality.

## Responsibilities

1. **Build Verification**: Ensure project compiles cleanly with dotnet build
2. **Process Management**: Kill existing nyxWM.exe processes before testing
3. **Application Launch**: Start the newly built nyxWM.exe
4. **Functionality Validation**: 
   - Check that WinAPI hooks are properly installed
   - Verify hotkey functionality works
   - Confirm window management behavior is correct
   - Validate game mode/pause functionality
5. **Log Analysis**: Monitor application output for expected behavior and error conditions
6. **Cleanup**: Properly terminate test processes after validation

## Process

1. **Build Check**: Run dotnet build and verify 0 errors and 0 new warnings
2. **Process Cleanup**: 
   - Find any existing nyxWM.exe processes
   - Terminate them gracefully (or forcefully if needed)
3. **Application Start**:
   - Launch the newly built nyxWM.exe from bin/Debug/net10.0-windows/ or appropriate output path
   - Allow sufficient startup time
4. **Validation Checks**:
   - Verify process is running
   - Check for expected console output indicating hook installation
   - Test hotkey responsiveness (if automated testing is possible)
   - Validate that window management functions correctly
   - Confirm game mode/pause functionality works as expected
5. **Monitoring**: 
   - Run application for a test period
   - Monitor logs for errors or unexpected behavior
   - Verify WinAPI hooks remain active
6. **Cleanup**: Terminate test nyxWM.exe process

## Validation Script Approach

Create PowerShell scripts to:
- Automate process killing: Stop-Process -Name nyxWM -Force
- Start application: Start-Process -FilePath "path\to\nxyWM.exe"
- Wait for startup: Start-Sleep -Seconds 3
- Check process status: Get-Process nyxWM -ErrorAction SilentlyContinue
- Monitor console output for key indicators
- Test specific functionality via automated input (if feasible)
- Validate hook installation through process inspection or test utilities

## Success Criteria

- [ ] Project builds with 0 errors and 0 new warnings
- [ ] nyxWM.exe launches successfully
- [ ] Application initializes without critical errors
- [ ] WinAPI hooks are properly installed and functional
- [ ] Hotkeys register and respond correctly
- [ ] Window management functions operate as expected
- [ ] Game mode/pause functionality works correctly
- [ ] No unhandled exceptions or crashes during test period

## Output Format

Report on:
- Build status (success/failure, error/warning counts)
- Process management (processes killed, started)
- Startup validation (successful launch, initialization checks)
- Functionality test results (what was tested, pass/fail)
- Log analysis (key observations, errors found)
- Overall test result (PASS/FAIL)
- Recommendations for next steps

## Example Usage

User: "/tester" (after /coder has implemented changes)

Agent would:
1. Run dotnet build and report results
2. Kill any existing nyxWM.exe processes
3. Start the newly built nyxWM.exe
4. Validate basic functionality and hook installation
5. Monitor for errors during a test period
6. Clean up test processes
7. Report comprehensive test results