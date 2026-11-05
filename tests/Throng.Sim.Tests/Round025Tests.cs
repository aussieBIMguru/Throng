using System.Diagnostics;
using System.Globalization;
using System.Text;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 025: step timings by phase (D-380), the stadium demos (D-381, D-404) and the stadium bench (report only,
/// D-405). The bench runs only with THRONG_BENCH=1 (bench.bat at the repo root): it takes minutes.
/// </summary>
[TestClass]
public sealed class Round025Tests
{
    /// <summary>MSTest's context, for the bench's report.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PhaseTimings_AreKept_AndDontChangeTheRun()
    {
        SimModel model = SimTestStudies.Compile(SimTestStudies.Corridor(ratePerMinute: 40));
        var a = new SimEngine(model);
        var b = new SimEngine(model);
        for (int s = 0; s < 300; s++)
        {
            a.Step();
            b.Step();
        }

        Assert.AreEqual(300, a.PerfSteps);
        var ticks = new long[SimEngine.PerfPhases.Count];
        a.CopyPhaseTicks(ticks);
        Assert.IsTrue(ticks.All(t => t >= 0) && ticks.Sum() > 0, "Time was measured.");
        Assert.AreEqual(a.Stats, b.Stats, "Timing never changes the run.");
        a.Reset();
        Assert.AreEqual(0, a.PerfSteps, "Reset starts the timings again.");
    }

    [TestMethod]
    public void StadiumDemos_ValidateWithoutErrors_AndCompile()
    {
        foreach (Study study in new[] { StadiumBuilder.BuildSector(), StadiumBuilder.Build() })
        {
            IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
            Assert.AreEqual(0, findings.Count(f => f.Severity == ValidationSeverity.Error),
                string.Join("; ", findings.Where(f => f.Severity == ValidationSeverity.Error).Select(f => f.Message)));
            SimTestStudies.Compile(study);
        }

        Study full = StadiumBuilder.Build();
        Assert.AreEqual(2, full.Levels.Count);
        Assert.AreEqual(StadiumBuilder.FullSectors * 3, full.Devices.Count, "Three turnstile lines per sector.");
        Assert.AreEqual(StadiumBuilder.FullSectors * 3, full.Regions.Count(r => r.Name.StartsWith("Seats ", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("Slow")] // long run: skipped by publish.ps1 unless -AllTests (D-430)
    public void StadiumSector_FansGetThroughTheTurnstilesAndUpstairs()
    {
        // Egress from the start fills the model fastest; the ingress scenario is checked on its own for a few minutes.
        var engine = new SimEngine(SimTestStudies.Compile(StadiumBuilder.Build(1, StadiumScenario.Ingress)));
        Run(engine, 900);
        Assert.IsTrue(engine.Stats.Spawned > 100, $"{engine.Stats.Spawned} fans arrived in 15 minutes.");
        Assert.IsTrue(engine.Stats.Arrived > 10, $"{engine.Stats.Arrived} reached their seats.");
        Assert.IsTrue(engine.Stats.StuckRemoved <= engine.Stats.Spawned / 50, $"{engine.Stats.StuckRemoved} stuck.");
    }

    /// <summary>
    /// The stadium bench (D-380, D-405): six sectors emptying (the most people at once), then one sector live-sized.
    /// Prints people, ms per step by phase and the speed against real time every simulated minute; fails only when the
    /// run breaks (an exception, or more than 2 % removed as stuck). Paste its output back into the chat.
    /// </summary>
    [TestMethod]
    [TestCategory("Bench")]
    public void Bench_Stadium()
    {
        if (Environment.GetEnvironmentVariable("THRONG_BENCH") != "1")
        {
            Assert.Inconclusive("The stadium bench runs only with THRONG_BENCH=1 (bench.bat).");
        }

        var report = new StringBuilder();
        foreach ((string label, int sectors, double minutes) in new[] { ("6 sectors, egress", StadiumBuilder.FullSectors, 15.0), ("1 sector, egress", 1, 12.0) })
        {
            BenchOne(report, label, sectors, minutes);
        }

        TestContext.WriteLine(report.ToString());
        Console.WriteLine(report.ToString());
    }

    private static void BenchOne(StringBuilder report, string label, int sectors, double minutes)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var setup = Stopwatch.StartNew();
        SimModel model = SimTestStudies.Compile(StadiumBuilder.Build(sectors, StadiumScenario.Egress));
        var engine = new SimEngine(model);
        report.Append(c, $"== {label}: compile {setup.Elapsed.TotalSeconds:0.0} s, step {model.TimeStepS:0.00} s").AppendLine();
        report.AppendLine("  sim min | people | ms/step | x real | " + string.Join(" | ", SimEngine.PerfPhases));

        int stepsPerMinute = (int)Math.Round(60 / model.TimeStepS);
        var last = new long[SimEngine.PerfPhases.Count];
        var now = new long[SimEngine.PerfPhases.Count];
        var wall = Stopwatch.StartNew();
        int peak = 0;
        double peakStepMs = 0;
        for (int minute = 1; minute <= minutes; minute++)
        {
            double started = wall.Elapsed.TotalMilliseconds;
            for (int s = 0; s < stepsPerMinute; s++)
            {
                engine.Step();
            }

            double ms = (wall.Elapsed.TotalMilliseconds - started) / stepsPerMinute;
            engine.CopyPhaseTicks(now);
            IEnumerable<string> phases = now.Select((t, p) => ((t - last[p]) * 1000.0 / Stopwatch.Frequency / stepsPerMinute).ToString("0.00", c));
            Array.Copy(now, last, now.Length);
            int people = engine.Stats.InSystem;
            if (people > peak)
            {
                peak = people;
                peakStepMs = ms;
            }

            report.Append(c, $"  {minute,7} | {people,6} | {ms,7:0.00} | {model.TimeStepS * 1000 / Math.Max(1e-9, ms),6:0.0} | ").AppendJoin(" | ", phases).AppendLine();
        }

        SimStats stats = engine.Stats;
        report.Append(c, $"  peak {peak:N0} people at {peakStepMs:0.00} ms/step (x{model.TimeStepS * 1000 / Math.Max(1e-9, peakStepMs):0.0} real time); {stats.Spawned:N0} spawned, {stats.Arrived:N0} out, {stats.StuckRemoved:N0} stuck; wall {wall.Elapsed.TotalSeconds:0} s").AppendLine();
        Assert.IsTrue(stats.StuckRemoved <= Math.Max(10, stats.Spawned / 50), $"{label}: {stats.StuckRemoved} removed as stuck of {stats.Spawned}.");
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS, MidpointRounding.AwayFromZero);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }
    }
}
