---
agent: 'agent'
description: Plan & Checklist Workflow for task execution
---
# Plan & Checklist Workflow

@_common-rules.prompt.md

> **MANDATORY**: Create a PLAN file FIRST before any other action.

General-purpose task tracking with a PLAN file.

## Rules
- **Always** create a PLAN file at "C:\root\repos\AIStuff\prompts\plan" first `investigate-<task>-<timestamp>.md`
- Break down into phases with specific, actionable steps
- Execute steps one at a time, updating checklist: `[ ]` → `[x]`
- Document findings in "Results & Findings" section

## PLAN File Template
```markdown
# Task: <Task Name>

## Overview
<Purpose in 1-2 sentences>

## Checklist

### Phase 1: <Phase Name>
- [ ] Step 1.1: <Specific action>
- [ ] Step 1.2: <Specific action>

### Phase 2: <Phase Name>
- [ ] Step 2.1: <Specific action>

## Results & Findings
<Fill after completion>

## Next Steps
<Add as needed>
```

## When to Use
- Development tasks
- Investigations
- Multi-step work requiring tracking
