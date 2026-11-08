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

This gives you the `s3l-mcp-plans` command (`~/.dotnet/tools` must be on `PATH`): a native program on macOS, Linux
and Windows, a framework-dependent one elsewhere — `dotnet tool` picks the package of the machine. `dnx Snail.MCP.Plans
--yes` runs it without installing.

**2. Connect** it under the name `plans`:

```bash
claude mcp add plans -- s3l-mcp-plans
```

Or, in a client that reads `mcp.json`:

```json
{
  "mcpServers": {
    "plans": {
      "command": "s3l-mcp-plans"
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

## What the tools answer

Every answer is the envelope every Snail server answers in: one block of JSON with an `ok` flag. On success `data.text`
is the plan as the agent reads it, and beside it go the ids and the version the next call takes, so nothing has to be
read out of the text:

```json
{"ok":true,"data":{"planId":"0199…","version":3,"planStatus":"Active","stepId":"0199…","status":"Done","nextStepId":"0199…","text":"Done: write the store\nprogress: 2 steps — 1 Done, …\n\nnext step — 0199…: wire the tools\n"}}
```

`create_plan`, `get_current_plan` and `revise_plan` answer `planId`, `version`, `status` and `text` (`create_plan` also
`drafted`, false when it handed back the plan already active); `update_step` the above; `list_active_plans` `count` and
`text`, with `unreadableRows` when the file holds rows it could not read. A refusal is flagged as an error and says
why, what to do and its kind — the word a hook or a script branches on, never the error's wording:

```json
{"ok":false,"error":"The plan changed since you read it, so nothing was written.","hint":"read it again with get_current_plan and redo this change on the current version","type":"versionConflict"}
```

The kinds: `noPlan`, `planNotFound`, `stepNotFound`, `versionConflict`, `transition`, `stepInProgress`, `settled`,
`notYours`, `unreadable`, `outraced`, and `arguments` for a value the tool cannot work with.

## On long plans

Finished steps print their notes shortened, so a plan of sixty steps still shows every step id. A revision
names the ids it just created before printing the plan, so you never have to hunt for them. The printed text is
capped at 20 000 characters and says so when cut.

## Configuration

Everything is optional. Settings come from the settings file, `~/.config/s3l-mcp-plans/config.json`, with the names
below without the prefix (`{ "AgentId": "…" }`), and from `SNAIL_MCP_PLANS_*` environment variables, which win. The file
is found by the one chain of every Snail server (Snail.Sdk's `ServerSettings`): a file named in `SNAIL_MCP_PLANS_CONFIG`
is the only one read, and when it is missing no file is; otherwise the first that exists of `./.s3l-mcp-plans.json`,
`./s3l-mcp-plans.json`, `~/.config/s3l-mcp-plans/config.json`, `~/.s3l-mcp-plans.json`. A machine's settings — and any
secret above all — belong in that file (`chmod 600`), never in a client's registration, which travels wherever it is
copied.

| Variable | Default | Meaning |
| --- | --- | --- |
| `SNAIL_MCP_PLANS_AGENTID` | the folder the server runs in | Who the plans belong to. Two sessions started in the same folder share a plan on purpose — the second continues the first. |
| `SNAIL_MCP_PLANS_DBPATH` | `~/.s3l-mcp-plans/plans.db` | The SQLite file. Point several clients at one file to see each other's plans. |
| `SNAIL_MCP_PLANS_BUSYTIMEOUTSECONDS` | `10` | How long a write waits when another session is writing. |
| `SNAIL_MCP_PLANS_CONFIG` | — | An explicit settings file, the only one read: when it is missing, no file is. |

The database is opened in write-ahead logging mode, so a second session reads it while the first one writes.

## Building from source

```bash
dotnet test    # 172 tests, including contract tests over the real stdio process
dotnet run --project src
```

Published from the repository beside the other Snail MCP servers, it lives in `~/.s3l-mcp/Snail.MCP.Plans`, and
`current` follows every later publication:

```bash
dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true
claude mcp add -s user plans -- ~/.s3l-mcp/Snail.MCP.Plans/current/Snail.MCP.Plans
```

Measured on an Apple Silicon Mac against `dotnet Snail.MCP.Plans.dll`: 24 MB resident at rest instead of 112, the
handshake answered in 60 ms instead of 206, and the first read of the plans in 5 ms instead of 345 — the database is
read with Microsoft.Data.Sqlite directly, in the tables and the format Entity Framework made, so an older file reads
the same.

## License

MIT
