# The plan: draft, walk, revise, watch

## <code>create_<wbr>plan</code>

Draft the plan BEFORE starting a task of more than one step: the objective, then the steps in the
order they are to be walked. Answers with data.planId, data.version and data.text — the plan printed
with the id of every step; those ids are what update_step takes.
One active plan per project: called while one is already active, it answers with that plan instead
of drafting a second (data.drafted false), so a retried call never leaves two behind.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `objective` | `string` | yes | — | What the work is meant to achieve. Immutable once the plan exists. |
| `steps` | `array` | yes | — | The steps, in the order they are to be walked. |
| `projectName` | `string` | — | `` | Project the plan belongs to. Empty — the folder this server runs in, which is the usual answer. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "objective": {
      "description": "What the work is meant to achieve. Immutable once the plan exists.",
      "type": "string"
    },
    "steps": {
      "description": "The steps, in the order they are to be walked.",
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "projectName": {
      "description": "Project the plan belongs to. Empty \u2014 the folder this server runs in, which is the usual answer.",
      "type": "string",
      "default": ""
    }
  },
  "required": [
    "objective",
    "steps"
  ]
}
```

</details>

## <code>get_<wbr>current_<wbr>plan</code>

The plan this agent is working on: data.text prints the objective, where it stands, and every step
with its status and id; data.planId and data.version are what revise_plan takes. Needs no
arguments — it answers by the identity of this server process; with no active plan it refuses
with type noPlan.
Call it at the start of a session and after losing context, before drafting anything: the plan you
cannot remember is still here, and drafting a second one for the same work loses the first.
Pass 'planId' to read any plan, including one another agent walks — that is how you look at a plan
from a different folder, which list_active_plans only summarises. Never call create_plan just to
look: it leaves a second plan behind.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `projectName` | `string` | — | `` | Project the plan belongs to. Empty — the folder this server runs in, which is the usual answer. |
| `planId` | `string` | — | `` | Read this plan instead, by its id — works for any agent's plan, read-only. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "projectName": {
      "description": "Project the plan belongs to. Empty \u2014 the folder this server runs in, which is the usual answer.",
      "type": "string",
      "default": ""
    },
    "planId": {
      "description": "Read this plan instead, by its id \u2014 works for any agent\u0027s plan, read-only.",
      "type": "string",
      "default": ""
    }
  }
}
```

</details>

## <code>update_<wbr>step</code>

Report a step and receive the next one. Call it when you start a step, and again the moment it
ends — data.text carries the new progress and the next Pending step, whose id is data.nextStepId,
so this replaces asking what to do next.
Status is one of: Pending (not started), InProgress (taken up — counts an attempt), Done
(finished), Failed (attempted, did not work — may be attempted again), Skipped (deliberately not
done, and then a note saying why is required).
A step done in one go may go from Pending straight to Done or Failed: that call counts as taking it
up. A plan runs one step at a time: report the step in progress before taking up another. Took one up
by mistake? Report it Pending again — the attempt it already spent is kept, no failure is
invented. A step that is Done or Skipped is settled and does not move again.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `stepId` | `string` | yes | — | Id of the step, as the plan printed it. |
| `status` | `Pending` \| `InProgress` \| `Done` \| `Failed` \| `Skipped` | yes | — | Where the step now stands. |
| `note` | `string` | — | `` | What came out of the step, or why it failed. Required when skipping. |
| `projectName` | `string` | — | `` | Project the plan belongs to. Empty — the folder this server runs in, which is the usual answer. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "stepId": {
      "description": "Id of the step, as the plan printed it.",
      "type": "string"
    },
    "status": {
      "description": "Where the step now stands.",
      "type": "string",
      "enum": [
        "Pending",
        "InProgress",
        "Done",
        "Failed",
        "Skipped"
      ]
    },
    "note": {
      "description": "What came out of the step, or why it failed. Required when skipping.",
      "type": "string",
      "default": ""
    },
    "projectName": {
      "description": "Project the plan belongs to. Empty \u2014 the folder this server runs in, which is the usual answer.",
      "type": "string",
      "default": ""
    }
  },
  "required": [
    "stepId",
    "status"
  ]
}
```

</details>

## <code>revise_<wbr>plan</code>

Change the plan when the work turns out different from the draft — a step missing, a step worded
wrongly, an order that no longer holds. Revise it rather than quietly walking away from it.
Pass the version you last read; if the plan has changed since, nothing is written and the call is
refused with type versionConflict — read it again. Each change is one of: Add (a new step, at the
end or after the step named in 'after'), Edit (reword a step that has not settled), Skip (take a
step out of the work, 'reason' required — it stays on the record), Reorder (move a step after
another, or to the front).
Steps are never deleted, only Skipped. Optionally pass 'summary' to say where the work stands, and
'outcome' to settle the plan: Active (reopen it), Completed, Failed, Abandoned — on its own, with
no changes, it closes the plan. Left alone, a plan completes itself once no step is open.
Answers with the plan as it now stands and its new version; the ids of the steps it added are
printed first, before the plan.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `planId` | `string` | yes | — | Id of the plan to change. |
| `version` | `integer` | yes | — | Version of the plan you last read; a stale version writes nothing. |
| `changes` | `array` | — | `null` | The changes, applied in the order given. Empty — none, to settle the plan by its outcome alone. |
| `summary` | `string` | — | `` | Where the work stands now. Empty — left as it was. |
| `outcome` | `string` | — | `` | Settle the plan: Active, Completed, Failed or Abandoned. Empty — decided by the steps. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "planId": {
      "description": "Id of the plan to change.",
      "type": "string"
    },
    "version": {
      "description": "Version of the plan you last read; a stale version writes nothing.",
      "type": "integer"
    },
    "changes": {
      "description": "The changes, applied in the order given. Empty \u2014 none, to settle the plan by its outcome alone.",
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "kind": {
            "description": "What to do: add, edit, skip or reorder.",
            "type": "string",
            "enum": [
              "Add",
              "Edit",
              "Skip",
              "Reorder"
            ]
          },
          "title": {
            "description": "Step text \u2014 required by add and edit, ignored otherwise.",
            "type": "string",
            "default": ""
          },
          "step_id": {
            "description": "Step this change applies to \u2014 required by edit, skip and reorder.",
            "type": "string",
            "default": ""
          },
          "after": {
            "description": "Put the step right after this one. Empty \u2014 at the end for add, at the front for reorder.",
            "type": "string",
            "default": ""
          },
          "reason": {
            "description": "Why the step is skipped \u2014 required by skip, it stays on the record in its place.",
            "type": "string",
            "default": ""
          }
        },
        "required": [
          "kind"
        ]
      },
      "default": null
    },
    "summary": {
      "description": "Where the work stands now. Empty \u2014 left as it was.",
      "type": "string",
      "default": ""
    },
    "outcome": {
      "description": "Settle the plan: Active, Completed, Failed or Abandoned. Empty \u2014 decided by the steps.",
      "type": "string",
      "default": ""
    }
  },
  "required": [
    "planId",
    "version"
  ]
}
```

</details>

## <code>list_<wbr>active_<wbr>plans</code>

What every agent is working on right now: per plan the objective, step counts by status, the step
in progress, and how long since it last moved — that age is what shows an agent stuck or gone.
This is the one tool that answers about other agents, not the caller. Answers data.count and
data.text, which is capped at 20000 characters and says so when cut.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `projectName` | `string` | — | `` | Narrow to one project. Empty — every project. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "projectName": {
      "description": "Narrow to one project. Empty \u2014 every project.",
      "type": "string",
      "default": ""
    }
  }
}
```

</details>

