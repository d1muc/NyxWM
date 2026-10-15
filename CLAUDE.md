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

## Strict Execution Protocol (MANDATORY FOR ALL TASKS)

For ANY user prompt or feature request, you MUST NOT apply code changes directly without following the exact step-by-step pipeline:

1. **STEP 1: PLAN**
   - Execute the logic defined in `.claude/commands/plan.md`.
   - Write the exact breakdown and steps into `TODO_AGENT.md` BEFORE editing any project files.

2. **STEP 2: CODE**
   - Follow `.claude/commands/coder.md`[cite: 10].
   - Implement changes step-by-step strictly based on `TODO_AGENT.md`[cite: 10].
   - Always run `dotnet build` after modifications[cite: 1].

3. **STEP 3: TEST & DEPLOY**
   - Execute `.claude/commands/tester.md` or run `.claude/scripts/Test-nyxWM.ps1` via terminal[cite: 10].
   - Verify that `nyxWM.exe` restarts and operates correctly[cite: 10].

DO NOT SKIP STEPS. DO NOT EDIT CODE BEFORE UPDATING TODO_AGENT.md[cite: 10].

## Ultra-Strict Autonomous Execution Protocol (NO LAZINESS)

When assigned a task, you are fully responsible for the entire lifecycle: Planning, Coding, Building, and Live Testing. 

1. **Mandatory Live Testing:** 
   - You MUST NOT claim a task is done just because `dotnet build` passed.
   - You MUST execute `.claude/scripts/Test-nyxWM.ps1` (or your testing script) to deploy and launch the binary.
   - For UI, Tray, or Hook features where visual/system validation is needed, you must check process existence (`Get-Process -Name "nyxWM"`) and parse output logs to ensure no silent crashes occur.

2. **No Mocking / Half-Done Features:**
   - If a feature requires UI or System Tray integration, you must write the actual WinForms/WPF/P/Invoke code (e.g., `NotifyIcon`, ContextMenuStrip) so it physically exists in the codebase. Do not assume the user will code it.

3. **Strict Iteration:**
   - If `dotnet build` fails, fix it immediately. Do not ask the user.
   - If the process crashes on launch, read the exception, fix the code, rebuild, and re-test.