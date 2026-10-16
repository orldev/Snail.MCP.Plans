using BenchmarkDotNet.Attributes;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Formatting;

namespace Snail.MCP.Plans.Benchmarks;

/// <summary>Building the text get_current_plan and list_active_plans answer with.</summary>
[MemoryDiagnoser]
public class AnswerBenchmarks
{
    private Plan _plan = null!;

    private PlanListing _active = new([], 0, 0);

    [GlobalSetup]
    public void Setup()
    {
        _plan = Sample.Of(0, 60);
        _active = new PlanListing([.. Enumerable.Range(0, 12).Select(number => Sample.Of(number, 25))], 0, 0);
    }

    /// <summary>A plan of sixty steps, as get_current_plan prints it.</summary>
    [Benchmark]
    public string CurrentPlan() => PlanView.Render(_plan, Sample.Now);

    /// <summary>Twelve active plans of twenty-five steps, as list_active_plans prints them.</summary>
    [Benchmark]
    public string ActivePlans() => PlanView.Active(_active, Sample.Now);
}
