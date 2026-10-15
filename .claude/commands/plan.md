# Plan Agent (/plan)

Analyzes the nyxWM architecture, focusing on ConfigManager and WindowManager, breaks down implementation tasks into subtasks, and records them in TODO_AGENT.md.

## Responsibilities

1. **Architecture Analysis**: Examine the structure of nyxWM, particularly:
   - ConfigManager.cs - configuration loading, validation, and thread-safety
   - WindowManager.cs - window management, threading, hooks, and game mode logic
   - Program.cs - application entry point, hotkey registration, and timer setup

2. **Thread-Safety Verification**: Ensure all state modifications to WindowPositions use lock (_positionsLock) as specified in CLAUDE.md

3. **Task Decomposition**: Break down user requests into clear, actionable subtasks

4. **TODO Management**: Write and maintain TODO_AGENT.md with actionable items

## Process

When invoked with a task description:
1. Read and analyze relevant source files (ConfigManager.cs, WindowManager.cs, Program.cs)
2. Identify areas requiring modification based on the request
3. Verify thread-safety requirements (lock usage for _positionsLock)
4. Break the implementation into discrete, verifiable steps
5. Write/update TODO_AGENT.md with the planned steps
6. Reference any specific files, methods, or lines that need modification

## Output Format

TODO_AGENT.md should contain:
- Clear, numbered steps
- File paths and line numbers when applicable
- Thread-safety considerations
- Build verification steps (dotnet build)
- Estimated complexity for each step

## Example Usage

User: "/plan Add a new hotkey for minimizing all windows"

Agent would:
1. Analyze hotkey registration in Program.cs
2. Examine WindowManager for minimizing functionality
3. Check ConfigManager for new setting storage
4. Create TODO_AGENT.md with steps like:
   - Add new hotkey setting to ConfigManager.Hotkeys
   - Register hotkey in Program.RegisterConfiguredHotkeys
   - Implement minimize functionality in WindowManager
   - Add message filter handling in HotkeyMessageFilter
   - Verify thread-safety where applicable
   - Run dotnet build to verify no errors

Always adhere to the immutability and file organization principles from coding-style.md.