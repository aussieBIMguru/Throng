using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ GROUP (round 024b, D-388, D-394): a group template picked in LIBRARY ▸ GROUPS. Member rows are laid out
/// like mix lines (a profile, then how many, D-394); the first row leads (D-387). Built-ins are edited in place for this
/// study as an override (RESET TO BUILT-IN puts them back); the study's own groups can be deleted when no mix line uses
/// them. Looked up by id on every read (the first edit of a built-in swaps in the study's copy).
/// </summary>
public sealed class GroupDefinitionViewModel : ObservableObject
{
    private static readonly GroupTemplate Fallback = new() { Name = "(gone)" };
    private readonly MainViewModel _owner;

    /// <summary>Wraps the group template with this id.</summary>
    public GroupDefinitionViewModel(Guid id, MainViewModel owner)
    {
        Id = id;
        _owner = owner;
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateDefinition(Template));
        ResetCommand = new RelayCommand(() => _owner.ResetDefinition(Template));
        DeleteCommand = new RelayCommand(() => _owner.DeleteDefinition(Template));
        SaveToMineCommand = new RelayCommand(() => _owner.SaveToMyLibrary(Template));
        UpdateFromMineCommand = new RelayCommand(() => _owner.UpdateFromMyLibrary(Id));
        AddMemberCommand = new RelayCommand(AddMember);
        RebuildMembers();
    }

    /// <summary>Id of the group template.</summary>
    public Guid Id { get; }

    /// <summary>The group as the study sees it now (its copy, else yours, else the built-in).</summary>
    public GroupTemplate Template => StudyTemplates.FindGroup(_owner.Study, Id, _owner.Mine) ?? Fallback;

    /// <summary>Makes an editable copy with a new name and key.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Puts a modified built-in back to the built-in's values.</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>Deletes one of the study's own groups that no mix line uses.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>SAVE TO MY LIBRARY (D-342, D-388): copies this group (with its member profiles and journeys) into your library.</summary>
    public RelayCommand SaveToMineCommand { get; }

    /// <summary>UPDATE FROM MY LIBRARY (D-344): your library's version back onto the study's copy (one undo step).</summary>
    public RelayCommand UpdateFromMineCommand { get; }

    /// <summary>Adds a member row.</summary>
    public RelayCommand AddMemberCommand { get; }

    /// <summary>One row per member profile, the lead first.</summary>
    public ObservableCollection<GroupMemberRowViewModel> Members { get; } = [];

    /// <summary>True when the study's copy differs from your library's (D-344, D-351).</summary>
    public bool DiffersFromMine => _owner.MineDiffers(Id);

    /// <summary>The line about your library (MINE, DIFFERS…); empty when it isn't in your library.</summary>
    public string MineText => _owner.MineStatusText(Id);

    /// <summary>True when <see cref="MineText"/> shows.</summary>
    public bool HasMineText => MineText.Length > 0;

    /// <summary>True for built-ins and their study copies.</summary>
    public bool IsBuiltIn => StudyDefinitions.IsBuiltIn(Template);

    /// <summary>True when the study's copy of a built-in differs from it.</summary>
    public bool IsModified => StudyDefinitions.IsModified(_owner.Study, Template);

    /// <summary>True for the study's own (duplicated or new) groups.</summary>
    public bool IsOwn => !IsBuiltIn && _owner.Study.Library.Find(Id) is not null;

    /// <summary>Where it came from and what editing it does.</summary>
    public string SourceText => _owner.IsMineOnly(Id)
        ? "From your library. Edits change it for this study only (SAVE TO MY LIBRARY puts them back in your library)."
        : IsBuiltIn
        ? IsModified
            ? "Built-in, modified in this study. RESET TO BUILT-IN puts the shipped values back. Edits can be undone."
            : "Built-in. Edits change it for this study only (DUPLICATE makes a separate group instead)."
        : Template.Source == TemplateSource.Imported
            ? "Imported into this study. Edits can be undone."
            : "Your group, saved in this study. Edits can be undone.";

    /// <summary>How many mix lines spawn it.</summary>
    public string UsageText
    {
        get
        {
            int uses = StudyDefinitions.GroupUseCount(_owner.Study, Id);
            return uses switch
            {
                0 => "Not used by any mix line yet: pick it as the \"who\" of a spawn zone's mix line in SELECTION.",
                1 => "Used by 1 mix line.",
                _ => string.Create(CultureInfo.InvariantCulture, $"Used by {uses} mix lines."),
            };
        }
    }

    /// <summary>Group size and a note on the limits, e.g. "2–5 people (about 3.5 on average)".</summary>
    public string SizeText
    {
        get
        {
            GroupTemplate group = Template;
            CultureInfo c = CultureInfo.InvariantCulture;
            (int Min, int Max)[] rows = [.. group.Rows().Select(m => (m.Min, m.Max))];
            if (rows.Length == 0)
            {
                return "No members: mix lines with this group spawn single commuters (CHECK warns).";
            }

            string size = group.MinSize == group.MaxSize
                ? string.Create(c, $"{group.MinSize} people")
                : string.Create(c, $"{group.MinSize}–{group.MaxSize} people");
            string text = string.Create(c, $"{size} (about {GroupTemplate.MeanSize(rows):0.#} on average). The first row leads: their preferences steer the group's choices; anyone's hard needs apply to all.");
            int minimums = rows.Sum(r => Math.Max(0, r.Min));
            if (minimums > GroupTemplate.MaxPeople)
            {
                text += string.Create(c, $" The minimums add up to {minimums}: groups are cut to {GroupTemplate.MaxPeople} from the last rows.");
            }

            return text;
        }
    }

    /// <summary>Display name (blank rejected).</summary>
    public string Name
    {
        get => Template.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A group needs a name.";
                OnPropertyChanged(nameof(Name));
                return;
            }

            Edit("Rename group", t => t.Name = trimmed);
        }
    }

    /// <summary>Description (the LIBRARY tooltip).</summary>
    public string Description
    {
        get => Template.Description;
        set => Edit("Change group description", t => t.Description = (value ?? string.Empty).Trim());
    }

    /// <summary>Profile choices for the member rows: the study's, then yours, then built-ins.</summary>
    public IReadOnlyList<IdOption> ProfileOptions =>
        [.. StudyTemplates.AvailableProfiles(_owner.Study, _owner.Mine).Select(p => new IdOption(p.Id, p.Name))];

    /// <summary>Journey choices for the default journey (D-389): "none" (<see cref="Guid.Empty"/>) first.</summary>
    public IReadOnlyList<IdOption> DefaultJourneyOptions =>
        [new IdOption(Guid.Empty, "None: straight to a sink"), .. StudyTemplates.AvailableJourneys(_owner.Study, _owner.Mine).Select(j => new IdOption(j.Id, j.Name))];

    /// <summary>
    /// The journey the group follows on mix lines set to "(profile default)" (D-389); <see cref="Guid.Empty"/> for none.
    /// A built-in (or one of yours) is embedded in the study when picked.
    /// </summary>
    public Guid DefaultJourneyId
    {
        get => Template.DefaultJourneyId ?? Guid.Empty;
        set
        {
            Guid? next = value == Guid.Empty ? null : value;
            if (next == Template.DefaultJourneyId)
            {
                return;
            }

            if (next is { } id)
            {
                _owner.EmbedForUse(id);
            }

            Edit("Change group journey", t => t.DefaultJourneyId = next);
        }
    }

    /// <summary>Re-reads everything (after an edit or undo); rebuilds the rows only when their number changed.</summary>
    public void Refresh()
    {
        if (Members.Count != Template.Rows().Count())
        {
            RebuildMembers();
        }
        else
        {
            foreach (GroupMemberRowViewModel row in Members)
            {
                row.Refresh();
            }
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>The member row at an index, or a placeholder.</summary>
    internal GroupMember MemberAt(int index)
    {
        List<GroupMember> rows = [.. Template.Rows()];
        return index >= 0 && index < rows.Count ? rows[index] : new GroupMember();
    }

    /// <summary>Replaces one member row (undoable); a profile only in your library is embedded first.</summary>
    internal void SetMember(int index, GroupMember member, string description)
    {
        List<GroupMember> rows = [.. Template.Rows()];
        if (index < 0 || index >= rows.Count || rows[index] == member)
        {
            return;
        }

        if (member.ProfileId != rows[index].ProfileId)
        {
            _owner.EmbedForUse(member.ProfileId);
        }

        rows[index] = member;
        Edit(description, t => t.Members = rows);
    }

    /// <summary>Removes one member row (undoable).</summary>
    internal void RemoveMember(int index)
    {
        List<GroupMember> rows = [.. Template.Rows()];
        if (index < 0 || index >= rows.Count)
        {
            return;
        }

        rows.RemoveAt(index);
        Edit("Remove group member row", t => t.Members = rows);
    }

    /// <summary>Moves a member row up one (to the top makes it the lead; undoable).</summary>
    internal void MoveMemberUp(int index)
    {
        List<GroupMember> rows = [.. Template.Rows()];
        if (index <= 0 || index >= rows.Count)
        {
            return;
        }

        (rows[index - 1], rows[index]) = (rows[index], rows[index - 1]);
        Edit(index == 1 ? "Make group member row the lead" : "Move group member row up", t => t.Members = rows);
    }

    private void AddMember()
    {
        List<GroupMember> rows = [.. Template.Rows()];
        Guid profile = rows.LastOrDefault()?.ProfileId
            ?? ProfileOptions.FirstOrDefault()?.Id
            ?? Guid.Empty;
        rows.Add(GroupMember.Of(profile, 1, 1));
        Edit("Add group member row", t => t.Members = rows);
    }

    private void RebuildMembers()
    {
        Members.Clear();
        int count = Template.Rows().Count();
        for (int k = 0; k < count; k++)
        {
            Members.Add(new GroupMemberRowViewModel(this, k));
        }
    }

    private void Edit(string description, Action<GroupTemplate> change) => _owner.EditGroupDefinition(Id, description, change);
}

/// <summary>One member row of a group (D-394): a profile and how many of them, between a minimum and a maximum.</summary>
public sealed class GroupMemberRowViewModel : ObservableObject
{
    private readonly GroupDefinitionViewModel _owner;
    private readonly int _index;

    /// <summary>Wraps the member row at <paramref name="index"/>.</summary>
    public GroupMemberRowViewModel(GroupDefinitionViewModel owner, int index)
    {
        _owner = owner;
        _index = index;
        RemoveCommand = new RelayCommand(() => _owner.RemoveMember(_index));
        MoveUpCommand = new RelayCommand(() => _owner.MoveMemberUp(_index));
    }

    /// <summary>Removes the row.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Moves the row up one (the top row leads).</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>True for the first row (the lead, D-387).</summary>
    public bool IsLead => _index == 0;

    /// <summary>True when the row can move up.</summary>
    public bool CanMoveUp => _index > 0;

    /// <summary>"LEAD" on the first row, else empty.</summary>
    public string RoleText => IsLead ? "LEAD" : string.Empty;

    /// <summary>Profile choices.</summary>
    public IReadOnlyList<IdOption> ProfileOptions => _owner.ProfileOptions;

    /// <summary>The row's profile.</summary>
    public Guid ProfileId
    {
        get => _owner.MemberAt(_index).ProfileId;
        set
        {
            if (value != Guid.Empty)
            {
                _owner.SetMember(_index, _owner.MemberAt(_index) with { ProfileId = value }, "Change group member profile");
            }
        }
    }

    /// <summary>Fewest of this profile per group (0–8; raises the maximum to match).</summary>
    public int Min
    {
        get => _owner.MemberAt(_index).Min;
        set
        {
            GroupMember row = _owner.MemberAt(_index);
            int min = Math.Clamp(value, 0, GroupTemplate.MaxPeople);
            _owner.SetMember(_index, row with { Min = min, Max = Math.Max(min, row.Max) }, "Change group member minimum");
        }
    }

    /// <summary>Most of this profile per group (1–8; lowers the minimum to match).</summary>
    public int Max
    {
        get => _owner.MemberAt(_index).Max;
        set
        {
            GroupMember row = _owner.MemberAt(_index);
            int max = Math.Clamp(value, 1, GroupTemplate.MaxPeople);
            _owner.SetMember(_index, row with { Max = max, Min = Math.Min(row.Min, max) }, "Change group member maximum");
        }
    }

    /// <summary>Re-reads the row.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
