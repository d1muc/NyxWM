Plan to remove Claude Code mentions from commit history

1. Analyze commit history:
   - Check recent commits for Co-Authored-By lines and author info.
   - Determine if we need to change author name/email or just remove Co-Authored-By.

2. Choose method to rewrite history:
   - Use `git filter-repo` if available, else `git filter-branch` or `git rebase`.
   - We'll use `git filter-repo` as it's faster and safer.

3. Install git-filter-repo if needed (via pip).

4. Run filter-repo to:
   - Remove lines matching "Co-Authored-By:.*Claude" from commit messages.
   - Optionally change author name and email to user-provided values.

5. Verify rewritten history:
   - Check that commits no longer contain Claude mentions.
   - Ensure no unintended changes.

6. Force-push rewritten history to GitHub (if required):
   - Since rewriting changes commit hashes, need to push with `--force`.
   - Warn that this will overwrite remote history.

7. Final verification:
   - Confirm remote commits are clean.
   - Ensure local repo works.

Note: This operation rewrites history and is destructive. Should only be done if no one else has cloned the repo or if you are okay with force-pushing.