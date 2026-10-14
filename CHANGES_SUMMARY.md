# Summary of Changes

## ConfigManager.cs
- Updated to automatically create the config directory and default config file (`config/config.jsonc`) if it does not exist.
- Changed the hotkey configuration structure from separate `Modifiers` and `Key` properties to a single string property for each hotkey (e.g., `"mainmod + space"`).
- Added a `MainMod` property to the configuration (default: `"CONTROL+ALT"`) which is used to replace the `"mainmod"` placeholder in hotkey strings.
- Updated validation to check hotkey strings after replacing `mainmod` and splitting by `'+'`.
- Added helper methods to validate modifier and key parts.
- When the config file is missing, the default configuration is saved to disk.
- When invalid settings are found, the configuration is corrected and saved back to disk.

## Program.cs
- Updated hotkey registration to parse the new flat hotkey string format.
- Added `ParseHotkeyString` method that replaces `"mainmod"` with the actual `MainMod` value, splits by `'+'`, parses modifier parts and key part.
- Kept existing `ParseModifierString` and `ParseKeyString` methods for parsing individual parts (they now expect a single modifier or key without `'+'`).
- The hotkey registration now uses the parsed values from the configuration strings.

## Build Status
- The project builds successfully with 0 warnings and 0 errors.

## Note
These changes fulfill the user's request for:
1. Automatic creation of the config file on disk if missing.
2. Ultra-simple, flat hotkey configuration using strings like `"mainmod + space"`.
3. Parsing and replacement of `mainmod` with the configured main modifier.