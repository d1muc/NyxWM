# Implementation Plan for CameraSensitivity Feature

## Steps to Implement:

1. **Add CameraSensitivity property to ConfigSettings class**
   - File: ConfigManager.cs
   - Location: ConfigSettings class (around line 397-438)
   - Add: `public float CameraSensitivity { get; set; } = 1.0f;`

2. **Add validation for CameraSensitivity in ValidateAndApplyDefaults method**
   - File: ConfigManager.cs
   - Location: ValidateAndAroundDefaults method (around line 144-243)
   - Add validation: if (settings.CameraSensitivity <= 0) { reset to 1.0f; }

3. **Modify WindowManager.cs to use CameraSensitivity in UpdatePosition method**
   - File: WindowManager.cs
   - Location: UpdatePosition method (around line 513-589)
   - Specifically in the dragging section (lines 520-532): multiply stepX and stepY by ConfigManager.Current.CameraSensitivity

4. **Verify implementation with dotnet build**
   - Run: `dotnet build`
   - Ensure 0 errors and 0 warnings

## Thread-Safety Considerations:
- No additional thread-safety measures needed as ConfigManager.Current is already thread-safe
- The CameraSensitivity property will be read-only after configuration loading (immutable pattern)