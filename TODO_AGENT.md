# TODO for updating GameCacheManager.cs

## Objective
1. Embed a comprehensive default blacklist of 220+ non-game executables directly into GameCacheManager.cs.
2. Ensure blacklist.json and gamecache.json are automatically created and populated on disk if they are missing.

## Steps

### Step 1: Plan (current step)
- Write this TODO_AGENT.md file.

### Step 2: Code
- Modify Services/GameCacheManager.cs:
  a. Add a static list or array containing the default blacklist of executables (220+ items).
  b. Update the LoadBlacklist method to:
      - If the blacklist file exists, load it as before.
      - If the blacklist file does not exist, populate _blacklistedExes with the default blacklist and then save it to disk (creating the file).
  c. Update the LoadCache method to:
      - If the cache file exists, load it as before.
      - If the cache file does not exist, ensure _knownGames is empty (as before) and then save an empty cache to disk (creating the file with an empty list).
  d. Ensure that the cache directory is created (already done in static constructor).

### Step 3: Test & Deploy
- Run `dotnet build` to verify the solution builds without errors.
- Run the testing script: `.claude/scripts/Test-nyxWM.ps1` to deploy and launch the binary, verifying that the application works correctly and the cache/blacklist files are created as expected.

## Detailed Implementation

### Default Blacklist
We will create a list of common non-game executables, including but not limited to:
- Web browsers: chrome.exe, firefox.exe, msedge.exe, iexplore.exe, opera.exe, brave.exe, etc.
- Communication: discord.exe, slack.exe, teams.exe, skype.exe, zoom.exe, etc.
- Development: devenv.exe, code.exe, idea64.exe, pycharm64.exe, webstorm64.exe, eclipse.exe, etc.
- Office: winword.exe, excel.exe, powerpnt.exe, outlook.exe, etc.
- Utilities: notepad.exe, notepad++.exe, totalcmd.exe, winrar.exe, 7zfm.exe, etc.
- Multimedia: vlc.exe, spotify.exe, itunes.exe, etc.
- System: explorer.exe, cmd.exe, powershell.exe, wt.exe, etc.
- Gaming platforms: steam.exe, origin.exe, epicgameslauncher.exe, etc.
- Others: many more to reach 220+.

### File Creation
- In LoadBlacklist: if file not found, set _blacklistedExes to the default list and call SaveBlacklist().
- In LoadCache: if file not found, after ensuring _knownGames is empty, call SaveCache() to create an empty cache file.

Note: We must be cautious about concurrent access, but the static constructor runs only once.
