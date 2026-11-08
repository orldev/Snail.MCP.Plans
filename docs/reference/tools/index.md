# Tools

5 tools in 1 family.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

A plan your agent actually keeps: it drafts its steps before starting, walks them in order, marks each one as it goes, and from another session `list_active_plans` shows every agent's objective, its step counts, the step in progress and how long since it last moved. Everything is local: one SQLite file, no accounts, no cloud, no API keys.

| Tool | Family | What it does |
| --- | --- | --- |
| [`create_plan`](plans.md#create_plan) | The plan: draft, walk, revise, watch | Draft the plan BEFORE starting a task of more than one step: the objective, then the steps in the order they are to be walked. |
| [`get_current_plan`](plans.md#get_current_plan) | The plan: draft, walk, revise, watch | The plan this agent is working on: data.text prints the objective, where it stands, and every step with its status and id; data.planId and data.version are w… |
| [`update_step`](plans.md#update_step) | The plan: draft, walk, revise, watch | Report a step and receive the next one. |
| [`revise_plan`](plans.md#revise_plan) | The plan: draft, walk, revise, watch | Change the plan when the work turns out different from the draft — a step missing, a step worded wrongly, an order that no longer holds. |
| [`list_active_plans`](plans.md#list_active_plans) | The plan: draft, walk, revise, watch | What every agent is working on right now: per plan the objective, step counts by status, the step in progress, and how long since it last moved — that age is… |
