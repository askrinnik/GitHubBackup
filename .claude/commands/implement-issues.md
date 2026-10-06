---
description: Take several small GitHub issues one after another on one branch, each with the full implement-issue workflow and its own commit, then verify once and ship one pull request
argument-hint: "<issue numbers in order> [--review-plans] [--ship]"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, PowerShell, Agent, AskUserQuestion, mcp__context7__resolve-library-id, mcp__context7__query-docs, mcp__microsoft-learn__microsoft_docs_search, mcp__microsoft-learn__microsoft_docs_fetch
---

Take the GitHub issues `$ARGUMENTS` of `askrinnik/GitHubBackup` in the given order, each with the `implement-issue` workflow and its own commit on one shared branch, then verify the branch once and ship one pull request.

- If `$ARGUMENTS` contains no issue numbers, recommend a batch with the `next-issue` skill and ask which to take before doing anything else.
- `--review-plans` restores the plan-review stops; `--ship` pre-authorises push, pull request and issue comments.

Follow the full batch workflow in @.ai/prompts/implement-issues.md.
