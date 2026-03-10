# Prompt Common Rules

This file contains shared rules for all workflow prompts. Reference with `@_common-rules.prompt.md`.

> **MANDATORY**: You MUST create or read a PLAN file BEFORE doing any other work. This is non-negotiable. If no PLAN file exists, create one first. If continuing work, read the existing PLAN file first.

## Language Rules

- **PLAN file & Chat**: Use the user's language
- **Code, commits, docs, logs**: English only

## PLAN File Convention

**Filename**: `PLAN-<task-name>-<timestamp>.md` (e.g., `PLAN-issue123-analysis-20260211.md`)

**Location**: Repository root or `_agent/` folder

**Basic Structure**:
```markdown
# Task: <Task Name>

## Overview
<Purpose in 1-2 sentences>

## Checklist
<Phases and steps>

## Results & Findings
<Fill after completion>

## Next Steps
<Add as needed>
```

## Continuing from Previous Session

If continuing work from a previous session:
1. Search for existing PLAN files: `PLAN-*-*.md`
2. Read the most recent PLAN file related to the task
3. Resume from the last incomplete step
4. If the user mentions an issue number, search for `PLAN-*{issue-number}*.md`

## Context Handoff Between Prompts

When transitioning between prompts (e.g., `/issue-analysis` → `/dev-tdd`):
1. The PLAN file is the **primary context handoff mechanism**
2. Read the existing PLAN file before starting work
3. Use documented findings (Issue Summary, Code Analysis, Proposal) to guide implementation
4. Do not re-analyze what was already documented

## Execution Rules

- Execute steps **one at a time**
- Update checklist immediately: `[ ]` → `[x]`
- Document findings in "Results & Findings"

## Security Check (Before Push)

**STOP and check before pushing any commits**:

| Check | Items |
|-------|-------|
| Secrets | API keys, tokens, passwords, connection strings |
| Personal Info | Email addresses, credentials |
| Internal URLs | Private endpoints, internal IPs |

If found: notify user, add to `.gitignore`, create `.template` file.
