using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Throng.Core.Model;
using Throng.Core.Serialization;

namespace Throng.Core.Library;

/// <summary>
/// Device and agent profile definitions a study can edit (round 017, D-286–D-288). Built-ins are edited in place
/// for one study through a whole copy under the built-in's id (the embedded copy placing already makes; D-287);
/// DUPLICATE and NEW make the study's own definitions with new ids. Nothing here touches the undo stack: callers
/// add, replace and remove through it.
/// </summary>
public static class StudyDefinitions
{
    /// <summary>
    /// All device definitions a study can use: its own and embedded ones, then <paramref name="mine"/>'s not embedded
    /// (round 020, D-343), then built-ins not in either.
    /// </summary>
    public static IReadOnlyList<DeviceTemplate> AvailableDevices(Study study, TemplateLibrary? mine = null) =>
        StudyTemplates.Layered(study.Library.Devices, mine?.Devices, BuiltInLibrary.Instance.Devices);

    /// <summary>
    /// The device definition with this id: the study's copy first, then <paramref name="mine"/>'s, then the built-in;
    /// null when none.
    /// </summary>
    public static DeviceTemplate? FindDevice(Study study, Guid id, TemplateLibrary? mine = null) =>
        study.Library.Find(id) as DeviceTemplate ?? mine?.Find(id) as DeviceTemplate ?? BuiltInLibrary.Instance.Find(id) as DeviceTemplate;

    /// <summary>The built-in a definition shares its id with, or null for the study's own definitions.</summary>
    public static TemplateBase? BuiltInOf(TemplateBase template) => BuiltInLibrary.Instance.Find(template.Id);

    /// <summary>True for built-ins and their per-study copies (same id).</summary>
    public static bool IsBuiltIn(TemplateBase template) => BuiltInOf(template) is not null;

    /// <summary>
    /// True when the study's copy of a built-in differs from the built-in (D-287): the override RESET TO BUILT-IN
    /// undoes. False for the built-in itself, for pristine embedded copies and for the study's own definitions.
    /// </summary>
    public static bool IsModified(Study study, TemplateBase template)
    {
        if (BuiltInOf(template) is not { } builtIn || study.Library.Find(template.Id) is not { } copy || ReferenceEquals(copy, builtIn))
        {
            return false;
        }

        return !SameValues(copy, builtIn);
    }

    /// <summary>True when two definitions of the same type hold the same values (ids and sources aside).</summary>
    public static bool SameValues(TemplateBase a, TemplateBase b)
    {
        if (a.GetType() != b.GetType())
        {
            return false;
        }

        return string.Equals(ValuesJson(a), ValuesJson(b), StringComparison.Ordinal);
    }

    /// <summary>
    /// Copies every value of <paramref name="source"/> into <paramref name="target"/> (deep), except the id and the
    /// source, which say which definition the target is. Both must be the same type.
    /// </summary>
    public static void CopyValues(TemplateBase target, TemplateBase source)
    {
        if (target.GetType() != source.GetType())
        {
            throw new ArgumentException("Definitions must be the same type.", nameof(source));
        }

        TemplateBase copy = TemplateCloner.Clone(source);
        foreach (PropertyInfo property in WritableProperties(target.GetType()))
        {
            property.SetValue(target, property.GetValue(copy));
        }
    }

    /// <summary>
    /// The study's own copy of a definition, embedding a pristine copy of a built-in first when the study has none
    /// (the same copy placing makes, D-287). Returns the study's copy.
    /// </summary>
    public static T EnsureStudyCopy<T>(Study study, T template)
        where T : TemplateBase
    {
        if (study.Library.Find(template.Id) is T own)
        {
            return own;
        }

        return (T)study.Library.AddCopy(template, TemplateSource.Embedded);
    }

    /// <summary>
    /// An editable copy of a device definition (DUPLICATE): new id and key, the same values and type tag (so
    /// journeys still route to it), a name no other device definition uses. Not added to the library.
    /// </summary>
    public static DeviceTemplate CreateDeviceCopy(Study study, DeviceTemplate source)
    {
        var copy = (DeviceTemplate)TemplateCloner.Clone(source);
        Rekey(copy, "device", UniqueName(AvailableDevices(study).Select(d => d.Name), source.Name));
        copy.Description = string.IsNullOrWhiteSpace(source.Description) ? $"Copy of {source.Name}." : source.Description;
        return copy;
    }

    /// <summary>An editable copy of a profile (DUPLICATE): new id and key, the same values, a unique name.</summary>
    public static AgentProfileTemplate CreateProfileCopy(Study study, AgentProfileTemplate source)
    {
        var copy = (AgentProfileTemplate)TemplateCloner.Clone(source);
        Rekey(copy, "agent", UniqueName(StudyTemplates.AvailableProfiles(study).Select(p => p.Name), source.Name));
        copy.Description = string.IsNullOrWhiteSpace(source.Description) ? $"Copy of {source.Name}." : source.Description;
        return copy;
    }

    /// <summary>A new point device definition with neutral values (NEW). Not added to the library.</summary>
    public static DeviceTemplate CreateNewDevice(Study study)
    {
        var device = new DeviceTemplate
        {
            Kind = DeviceKind.Point,
            Category = "Devices",
            TypeTag = UniqueTag(study, "Custom"),
            Servers = 1,
            ServiceTime = Distribution.Uniform(5, 15),
            WidthM = 1.0,
            DepthM = 1.0,
            ColorHex = "#A3E635",
            Description = "A device of your own: set its type tag, service time and size.",
        };
        Rekey(device, "device", UniqueNameFrom(AvailableDevices(study).Select(d => d.Name), "New device"));
        return device;
    }

    /// <summary>A new profile with the class defaults (an adult commuter's numbers; NEW). Not added to the library.</summary>
    public static AgentProfileTemplate CreateNewProfile(Study study)
    {
        var profile = new AgentProfileTemplate
        {
            Category = "Agents",
            ColorHex = "#F472B6",
            Description = "A profile of your own: set its speed, size, patience and preferences.",
        };
        Rekey(profile, "agent", UniqueNameFrom(StudyTemplates.AvailableProfiles(study).Select(p => p.Name), "New profile"));
        return profile;
    }

    /// <summary>Placed devices whose definition is <paramref name="template"/> (by reference, as the simulation finds it).</summary>
    public static IReadOnlyList<Device> DevicesUsing(Study study, DeviceTemplate template) =>
        [.. study.Devices.Where(d => ReferenceEquals(StudyTemplates.FindDeviceTemplate(study, d), template))];

    /// <summary>Number of mix lines (every spawn zone's rules) that use a profile.</summary>
    public static int ProfileUseCount(Study study, Guid profileId)
    {
        int uses = 0;
        foreach (Region region in study.Regions)
        {
            foreach (EmissionRule rule in region.EmissionRules ?? [])
            {
                foreach (MixLine line in rule?.Mix ?? [])
                {
                    if (line?.ProfileId == profileId)
                    {
                        uses++;
                    }
                }

                if (rule is not null && (rule.Mix is null || rule.Mix.Count == 0) && rule.LegacyProfileId == profileId)
                {
                    uses++;
                }
            }
        }

        return uses;
    }

    /// <summary>Device type tags the study knows: journey device nodes, placed devices and every device definition.</summary>
    public static IReadOnlyList<string> KnownDeviceTypes(Study study) =>
        [.. StudyTemplates.AvailableJourneys(study)
            .SelectMany(j => j.Nodes ?? [])
            .Where(n => n is { Kind: JourneyNodeKind.Device })
            .Select(n => n.TargetTag)
            .Concat(study.Devices.Select(d => d.TypeTag))
            .Concat(AvailableDevices(study).Where(d => d.Kind == DeviceKind.Point).Select(d => d.TypeTag))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    private static void Rekey(TemplateBase template, string prefix, string name)
    {
        Guid id = Guid.NewGuid();
        template.Id = id;
        template.Key = $"{prefix}.user.{id:N}";
        template.Name = name;
        template.Source = TemplateSource.User;
        template.Version = 1;
    }

    private static string UniqueName(IEnumerable<string> existing, string sourceName)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = $"{sourceName} (copy)";
        for (int n = 2; names.Contains(name); n++)
        {
            name = $"{sourceName} (copy {n.ToString(CultureInfo.InvariantCulture)})";
        }

        return name;
    }

    private static string UniqueNameFrom(IEnumerable<string> existing, string baseName)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = baseName;
        for (int n = 2; names.Contains(name); n++)
        {
            name = $"{baseName} {n.ToString(CultureInfo.InvariantCulture)}";
        }

        return name;
    }

    private static string UniqueTag(Study study, string baseTag)
    {
        var tags = KnownDeviceTypes(study).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string tag = baseTag;
        for (int n = 2; tags.Contains(tag); n++)
        {
            tag = baseTag + n.ToString(CultureInfo.InvariantCulture);
        }

        return tag;
    }

    private static IEnumerable<PropertyInfo> WritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                && p.Name is not nameof(TemplateBase.Id) and not nameof(TemplateBase.Source));

    private static string ValuesJson(TemplateBase template)
    {
        TemplateBase copy = TemplateCloner.Clone(template);
        copy.Id = Guid.Empty;
        copy.Source = TemplateSource.User;
        return JsonSerializer.Serialize(copy, copy.GetType(), JsonDefaults.Options);
    }
}
