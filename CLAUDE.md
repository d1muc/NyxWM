Strict Guidelines for Claude Agent (nyxWM Project)

Core Editing Rules

STRICT SCOPE ADHERENCE (CRITICAL)

NEVER modify, rewrite, format, or "clean up" code, classes, methods, or files that were not explicitly requested in the prompt.

If the task asks to change or implement Feature A, you must ONLY edit code directly required for Feature A.

Do NOT perform opportunistic refactoring, style fixes, or architectural reorganizations outside the immediate task scope.

NO ASSUMPTIONS / NO UNAUTHORIZED CHANGES

Do not add features, change default configurations, or alter existing keybindings unless explicitly instructed.

Preserve existing method signatures, class structures, and field names unless a change is directly required by the user prompt.

BUILD & VALIDATION REQUIREMENT

After applying any code modifications, always verify that the project builds cleanly using:

dotnet build


Ensure there are 0 compilation errors and no new warnings introduced by your changes.

THREAD SAFETY & ARCHITECTURE INTEGRITY

All state modifications touching c  WindowPositions in WindowManager.cs) MUST be protected using lock (_positionsLock).

Maintain the established single-re  wManager, ConfigManager, Program, and utility files.

CONCURRENCY & HOOK PRESERVATION

Do not remove existing synchroniza  ext calls, or Low-Level Mouse/WinEvent hooks without explicit instructions.


