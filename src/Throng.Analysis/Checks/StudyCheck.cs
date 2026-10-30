using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim;

namespace Throng.Analysis.Checks;

/// <summary>
/// The full CHECK (round 016, D-271–D-274): the static rules (<see cref="StudyValidator"/>), compiling the navigation
/// world with its reachability checks (<see cref="SimSetup"/>), then, when the study compiles, the static capacity
/// check, escalator direction and unused devices (<see cref="CapacityCheck"/>), and narrow gaps
/// (<see cref="NarrowGapCheck"/>). Every finding carries its rule. Never throws for bad studies.
/// </summary>
public static class StudyCheck
{
    /// <summary>Runs every rule over <paramref name="study"/>; findings come back errors first.</summary>
    public static IReadOnlyList<ValidationMessage> Run(Study study, IPolygonOps? ops = null, CancellationToken cancel = default)
    {
        ops ??= ClipperPolygonOps.Instance;
        SimSetupResult setup = SimSetup.Build(study, ops, cancel);
        var findings = new List<ValidationMessage>(Normalise(setup.Findings));
        if (setup.Model is { } model)
        {
            cancel.ThrowIfCancellationRequested();
            findings.AddRange(CapacityCheck.Run(study, model));
        }

        cancel.ThrowIfCancellationRequested();
        double radius = setup.Model is { } m && m.Profiles.Count > 0 ? m.Profiles.Max(p => p.Template.RadiusM) : 0.25;
        findings.AddRange(NarrowGapCheck.Run(study, ops, radius));
        return Sorted(findings);
    }

    /// <summary>Gives compile findings no rule claimed the <see cref="ValidationRules.Setup"/> rule.</summary>
    public static IReadOnlyList<ValidationMessage> Normalise(IReadOnlyList<ValidationMessage> findings)
    {
        var list = new List<ValidationMessage>(findings);
        StudyValidator.Tag(list, 0, ValidationRules.Setup);
        return list;
    }

    /// <summary>True when the study's ignore list holds this finding (D-272).</summary>
    public static bool IsIgnored(Study study, ValidationMessage finding) => study.IgnoredFindings.Contains(finding.IgnoreKey);

    /// <summary>Errors first, then warnings, then info, each in rule-catalogue order.</summary>
    private static List<ValidationMessage> Sorted(List<ValidationMessage> findings)
    {
        var order = ValidationRules.All.Select((r, i) => (r.Id, i)).ToDictionary(p => p.Id, p => p.i);
        return [.. findings
            .Select((f, i) => (f, i))
            .OrderByDescending(p => p.f.Severity)
            .ThenBy(p => order.TryGetValue(p.f.Rule, out int k) ? k : int.MaxValue)
            .ThenBy(p => p.i)
            .Select(p => p.f)];
    }
}
