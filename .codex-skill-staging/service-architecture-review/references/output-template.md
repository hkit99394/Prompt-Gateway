# Output Template

Use this structure when the user asks for a full review.

## Architecture Review

- Summarize the system shape in a few paragraphs.
- Review by subsystem or runtime boundary, not by file inventory.
- Call out what is already strong before discussing weaknesses.

## Risk Register

For each major risk:

- `Label`
- `Severity`
- `Likelihood`
- `Why it matters`
- `Evidence`
- `Mitigation direction`

Order by practical impact, not by category.

## Concrete Refactor Plan

Use a staged sequence:

1. foundational or prerequisite refactors
2. behavior or runtime changes
3. observability and rollout reinforcement
4. cleanup and decommissioning

For each stage, include:

- objective
- main code/infra areas affected
- dependencies
- success criteria

If the user wants a shorter answer, compress this into a few paragraphs while keeping the same logic.
