---
description: Take a GitHub issue end to end — dependencies and PRD check, plan with review, implementation with tests, verification, issue comment, pull request, and the next-issue recommendation
argument-hint: "[issue number] [optional: 'stay on current branch']"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, PowerShell, Agent, AskUserQuestion, mcp__context7__resolve-library-id, mcp__context7__query-docs, mcp__microsoft-learn__microsoft_docs_search, mcp__microsoft-learn__microsoft_docs_fetch
---

Take GitHub issue `$ARGUMENTS` of `askrinnik/GitHubBackup` end to end.

- If `$ARGUMENTS` contains no issue number, recommend one with the `next-issue` skill and ask which to take before doing anything else.
- If `$ARGUMENTS` asks to stay on the current branch (for example "stay", "no switch", "continue on the current branch"), skip the switch to `main` in step 0.

Follow the full workflow in @.ai/prompts/implement-issue.md.
