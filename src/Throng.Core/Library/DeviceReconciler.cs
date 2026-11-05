using Throng.Core.Model;

namespace Throng.Core.Library;

/// <summary>
/// Device definition exchange (round 017, D-290): builds the bundle EXPORT writes, finds whether an imported
/// definition's type tag means anything in the study, and applies the answer.
/// </summary>
public static class DeviceReconciler
{
    /// <summary>A bundle holding a deep copy of the definition.</summary>
    public static DeviceBundle BuildBundle(DeviceTemplate device) =>
        new() { Device = (DeviceTemplate)TemplateCloner.Clone(device) };

    /// <summary>
    /// What needs an answer: the definition's type tag when no journey step, placed device or other definition in
    /// the study uses it (map options: the types it does know). Empty when the tag is known or blank.
    /// </summary>
    public static List<ReconcileItem> Find(Study study, DeviceBundle bundle)
    {
        string tag = (bundle.Device.TypeTag ?? string.Empty).Trim();
        if (tag.Length == 0 || bundle.Device.Kind != DeviceKind.Point)
        {
            return [];
        }

        IReadOnlyList<string> known = StudyDefinitions.KnownDeviceTypes(study);
        if (known.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            return [];
        }

        return [new ReconcileItem(ReconcileKind.ImportedDeviceType, tag, $"device '{bundle.Device.Name}'", known)];
    }

    /// <summary>
    /// The definition to add: a copy marked Imported, with a new id, key and "(imported)" name only when the id is
    /// taken, and its type tag mapped when the answer says so (keep and drop both keep it).
    /// </summary>
    public static DeviceTemplate Apply(Study study, DeviceBundle bundle, IReadOnlyList<ReconcileItem> items, IReadOnlyList<ReconcileChoice> choices)
    {
        var device = (DeviceTemplate)TemplateCloner.Clone(bundle.Device);
        for (int k = 0; k < items.Count && k < choices.Count; k++)
        {
            if (items[k].Kind == ReconcileKind.ImportedDeviceType && choices[k] is { Action: ReconcileAction.Map, MapTo: { } to } && to.Trim().Length > 0)
            {
                device.TypeTag = to.Trim();
            }
        }

        device.Source = TemplateSource.Imported;
        if (study.Library.Find(device.Id) is not null || BuiltInLibrary.Instance.Find(device.Id) is not null
            || StudyDefinitions.AvailableDevices(study).Any(d => string.Equals(d.Key, device.Key, StringComparison.Ordinal)))
        {
            Guid id = Guid.NewGuid();
            device.Id = id;
            device.Key = $"device.imported.{id:N}";
            device.Name = $"{device.Name} (imported)";
        }

        return device;
    }
}
