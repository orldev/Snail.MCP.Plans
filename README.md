# Snail.MCP.Plans

A plan your agent actually keeps. Connect this MCP server to Claude Code, Claude Desktop, VS Code or any
other MCP client, and the agent drafts its steps before starting, walks them in order, marks each one as
it goes — and from another session you can ask what is running and how far along it is.

**What changes once it is connected:**

- The agent writes the plan down before it starts, instead of holding it in a conversation that gets
  summarised away.
- You clear the context mid-task; the agent asks for its plan back and carries on from the step it was on,
  rather than starting over or quietly doing something else.
- A step fails, and it stays failed and visible until the agent retries it or skips it with a reason. The
  plan is not a success story written afterwards.
- From a second session you ask `list_active_plans` and see every agent's objective, its step counts, the
  step in progress, and how long since it last moved — which is how you spot one that has stalled.

Everything is local: one SQLite file, no accounts, no cloud, no API keys.

## Quick start

**1. Install** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

```bash
dotnet tool install --global Snail.MCP.Plans --prerelease
```

This gives you the `snail-mcp-plans` command (`~/.dotnet/tools` must be on `PATH`).

**2. Connect** it under the name `plans`:

```bash
claude mcp add plans -- snail-mcp-plans
```

Or, in a client that reads `mcp.json`:

```json
{
  "mcpServers": {
    "plans": {
      "command": "snail-mcp-plans"
    }
  }
}
```

**3. That is all.** The server tells the agent how to use it during the MCP handshake, so there is nothing
to add to `CLAUDE.md` per project.

## The five tools

| Tool | What it is for |
| --- | --- |
| `create_plan` | Draft the objective and the steps before starting a multi-step task. |
| `get_current_plan` | Takes no arguments — answers with this agent's plan. The call to make after losing context. Pass `planId` to read any plan, including another agent's. |
| `update_step` | Report a step and receive the next one in the same answer. |
| `revise_plan` | Add, reword, skip or reorder steps as the work turns out different from the draft. |
| `list_active_plans` | What every agent is working on, and how far along. The one tool that answers about others. |

A step moves `Pending → InProgress → Done | Failed | Skipped`, a failed step may be attempted again, and
one taken up by mistake goes back to `Pending` without inventing a failure.
Steps are never deleted, only skipped with a reason — progress you read from another session cannot be
improved by dropping the inconvenient steps, which is also why the answer carries counts per status and
the age of the last write rather than a percentage.

A plan needs no closing call: it completes itself once no step is open, and reopens if you add one.

## On long plans

Finished steps print their notes shortened, so a plan of sixty steps still shows every step id. A revision
names the ids it just created before printing the plan, so you never have to hunt for them. Answers are
capped and say so when cut.

## Configuration

Everything is optional. Settings come from `SNAIL_MCP_PLANS_*` environment variables or from a
`snail-mcp-plans.json` file (looked up in the working directory, then `~/.config/snail-mcp-plans/config.json`,
then the profile); the environment wins.

| Variable | Default | Meaning |
| --- | --- | --- |
| `SNAIL_MCP_PLANS_AGENTID` | the folder the server runs in | Who the plans belong to. Two sessions started in the same folder share a plan on purpose — the second continues the first. |
| `SNAIL_MCP_PLANS_DBPATH` | `~/.snail-mcp-plans/plans.db` | The SQLite file. Point several clients at one file to see each other's plans. |
| `SNAIL_MCP_PLANS_BUSYTIMEOUTSECONDS` | `10` | How long a write waits when another session is writing. |
| `SNAIL_MCP_PLANS_CONFIG` | — | An explicit config file, overriding the lookup. |

The database is opened in write-ahead logging mode, so a second session reads it while the first one writes.

## Building from source

```bash
dotnet test    # 125 tests, including a contract test over the real stdio process
dotnet run --project src
```

## License

MIT
