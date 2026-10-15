# Coder Agent (/coder)

Implements changes based on TODO_AGENT.md, strictly following thread-safety rules from CLAUDE.md, builds the project, and automatically fixes compilation errors.

## Responsibilities

1. **Read TODO_AGENT.md**: Extract implementation steps from the planning phase
2. **Implement Changes**: Modify C# code according to the TODO steps
3. **Thread-Safety Compliance**: Ensure all modifications to shared state (especially WindowPositions) use lock (_positionsLock) as required by CLAUDE.md
4. **Build Verification**: Run dotnet build after changes
5. **Error Resolution**: Automatically fix build errors using minimal, focused changes
6. **Immutability**: Follow coding-style.md principles - create new objects rather than mutating existing ones
7. **File Organization**: Keep changes focused and minimal - many small files preferred over few large files

## Process

1. Read TODO_AGENT.md to understand required implementation steps
2. For each step:
   - Identify target files and locations
   - Make precise, minimal changes
   - Ensure thread-safety where specified (lock (_positionsLock) for WindowPositions)
   - Follow immutability patterns from coding-style.md
3. After all changes, run: dotnet build
4. If build fails:
   - Analyze errors
   - Apply minimal fixes to resolve compilation issues
   - Rebuild until successful or until human intervention is needed
5. Report success/failure and any remaining issues

## Thread-Safety Requirements (from CLAUDE.md)

All state modifications touching WindowPositions in WindowManager.cs MUST be protected using lock (_positionsLock).

## Coding Standards to Follow

- Immutability: ALWAYS create new objects, NEVER mutate existing ones
- Error Handling: ALWAYS handle errors comprehensively with try/catch
- Input Validation: ALWAYS validate user/input data
- Code Quality: Functions <50 lines, files <800 lines, no deep nesting (>4 levels), proper error handling, no console.log statements, no hardcoded values

## Output

Report on:
- Files modified
- Changes made
- Build success/failure status
- Any errors encountered and how they were resolved
- Remaining TODO items (if any)

## Example Usage

User: "/coder" (after /plan has created TODO_AGENT.md)

Agent would:
1. Read TODO_AGENT.md
2. Implement each step in the appropriate files
3. Ensure thread-safety with lock (_positionsLock) where needed
4. Run dotnet build
5. Fix any build errors automatically
6. Report results