# System Tray Implementation Plan

## Goal: Fix and complete system tray icon with context menu to nyxWM

## Steps:

1. **Analyze WindowManager.cs** to understand current pause state management
2. **Add SetPause(bool pause) method** to WindowManager.cs:
   - Set _isPaused = pause parameter
   - If pause is true, call _mouseHook?.Stop()
   - If pause is false, call _mouseHook?.Start()
3. **Analyze Program.cs** to understand current system tray implementation
4. **Fix Program.cs system tray implementation**:
   - Replace direct IsPaused property setting with windowManager.SetPause() calls
   - Update status item text based on actual pause state
   - Ensure proper event handling for all menu items
5. **Verify thread-safety** considerations for _isPaused access
6. **Build and verify** with `dotnet build` (0 errors, 0 warnings)
7. **Run test script** `.claude/scripts/Test-nyxWM.ps1` to confirm functionality

## Detailed Implementation:

### In WindowManager.cs:
- Add public void SetPause(bool pause) method after existing methods
- Method body:
  ```csharp
  _isPaused = pause;
  if (_isPaused)
  {
      _mouseHook?.Stop();
  }
  else
  {
      _mouseHook?.Start();
  }
  ```

### In Program.cs:
- In statusItem.Click handler:
  - Replace `windowManager.IsPaused = !windowManager.IsPaused;` with:
    ```csharp
    bool newState = !windowManager.IsPaused;
    windowManager.SetPause(newState);
    statusItem.Text = newState ? "Status: Paused" : "Status: Running";
    ```
- In toggleGameModeItem.Click handler:
  - Keep existing call to windowManager.ToggleGameMode()
  - Update statusItem.Text based on windowManager.IsPaused after toggle
- Ensure exitItem.Click handler properly cleans up resources

## Verification:
- dotnet build succeeds with no warnings
- Test script runs without errors
- System tray icon appears and menu functions correctly