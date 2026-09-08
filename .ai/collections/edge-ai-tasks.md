# Task Research and Planning

A tool-neutral workflow for researching, planning, and implementing complex
tasks in large repositories.

| Asset | Type | Purpose |
| --- | --- | --- |
| [Task researcher](../agents/task-researcher.agent.md) | Agent | Gather verified repository context without changing implementation files. |
| [Task planner](../agents/task-planner.agent.md) | Agent | Convert research into a phased, testable plan. |
| [Task implementation](../instructions/task-implementation.md) | Instruction | Execute approved task plans with progress tracking. |

Generated tracking files belong under `.copilot-tracking/` and should remain
specific to the consuming repository.
