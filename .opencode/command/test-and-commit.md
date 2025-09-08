# Test and Commit

Run all non-E2E tests, fix any issues found, and commit/push if all tests pass.

## Command

```bash
# Run all tests except E2E tests
dotnet test --filter "Category!=E2E" --verbosity normal

# If tests fail, Claude will analyze and fix the issues
# If all tests pass, commit and push changes
if [ $? -eq 0 ]; then
  echo "All tests passed! Committing and pushing changes..."
  git add .
  git commit -m "$(cat <<'EOF'
Fix test issues and update codebase

All non-E2E tests are now passing.

🤖 Generated with [Claude Code](https://claude.ai/code)

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
  git push
else
  echo "Tests failed. Claude will analyze and fix the issues."
fi
```

## Usage

Run this command when you want to:
1. Execute all tests except E2E tests
2. Have Claude automatically fix any test failures
3. Commit and push changes if all tests pass

This command ensures your codebase maintains test quality while automating the commit process for successful test runs.