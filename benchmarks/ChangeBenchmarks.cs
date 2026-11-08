using BenchmarkDotNet.Attributes;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Formatting;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Benchmarks;

/// <summary>What update_step and revise_plan do between reading a plan and storing it, on a store that keeps nothing.</summary>
[MemoryDiagnoser]
public class ChangeBenchmarks
{
    private PlanProgress _progress = null!;

    private PlanRevision _revision = null!;

    private Plan _plan = null!;

    private StepChange[] _changes = [];

    private string _inProgress = "";

    [GlobalSetup]
    public void Setup()
    {
        _plan = Sample.Of(0, 60);

        var store = new Sample.FixedStore(_plan);

        _progress = new PlanProgress(store, new Sample.Identity(), new Sample.Clock());
        _revision = new PlanRevision(store, new Sample.Identity(), new Sample.Clock());
        _inProgress = Sample.Id(0, 21).ToString();
        _changes =
        [
            new StepChange(ChangeKind.Add, Title: "check the published program", After: Sample.Id(0, 30).ToString()),
            new StepChange(ChangeKind.Edit, Title: "step 40, worded again", StepId: Sample.Id(0, 40).ToString()),
            new StepChange(ChangeKind.Skip, StepId: Sample.Id(0, 45).ToString(), Reason: "done by the step before"),
            new StepChange(ChangeKind.Reorder, StepId: Sample.Id(0, 50).ToString(), After: Sample.Id(0, 25).ToString())
        ];
    }

    /// <summary>The step in progress of a sixty-step plan reported done, the next one handed over.</summary>
    [Benchmark]
    public Task<Answer<PrintedStep>> UpdateStep() => _progress.AdvanceAsync(_inProgress, StepStatus.Done, "finished");

    /// <summary>An add, an edit, a skip and a reorder applied to a sixty-step plan, the plan printed back.</summary>
    [Benchmark]
    public Task<Answer<PrintedPlan>> RevisePlan() => _revision.ReviseAsync(_plan.Id.ToString(), _plan.Version, _changes);
}
