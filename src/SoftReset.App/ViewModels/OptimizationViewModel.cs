using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Optimization;
using SoftReset.Core.Safety;

namespace SoftReset.App.ViewModels;

public sealed partial class ActionCard : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public ActionCard(IOptimizationAction a) => Action = a;
    public IOptimizationAction Action { get; }
    public string Title => Action.Title;
    public string Description => Action.Description;
    public string Badges => Action.Reversibility switch { Reversibility.Full => "Can undo", Reversibility.Partial => "Can partly undo", _ => "Can't undo" };
}

public sealed class CategoryGroup
{
    public CategoryGroup(string name, IEnumerable<ActionCard> actions) { Name = name; Actions = actions.ToList(); }
    public string Name { get; }
    public IReadOnlyList<ActionCard> Actions { get; }
}

/// <summary>A tickable item (startup entry, file, process…) shown in a list.</summary>
public sealed partial class SelectableItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? State { get; init; }
    public string? Recommendation { get; init; }
    public string? Warning { get; init; }
    public bool Selectable { get; init; } = true;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class OptionItem : ObservableObject
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsCurrent { get; init; }
    public bool IsRecommended { get; init; }
    public string Hint => string.Join(" · ", new[] { IsCurrent ? "current" : null, IsRecommended ? "recommended" : null }.Where(x => x is not null));
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class OptimizationViewModel : PageViewModel
{
    public OptimizationViewModel(Shell shell) : base(shell)
    {
        string Name(OptimizationCategory c) => c switch
        {
            OptimizationCategory.Startup => "Faster startup",
            OptimizationCategory.Storage => "Free up space",
            OptimizationCategory.Background => "Background programs",
            OptimizationCategory.Power => "Power",
            OptimizationCategory.Gaming => "Gaming",
            OptimizationCategory.Display => "Display",
            OptimizationCategory.Maintenance => "Repair Windows",
            OptimizationCategory.AppMaintenance => "Apps",
            OptimizationCategory.Network => "Internet",
            OptimizationCategory.Desktop => "Look and feel",
            _ => c.ToString(),
        };
        Groups = S.Actions.Catalog.GroupBy(a => a.Category).OrderBy(g => g.Key).Select(g => new CategoryGroup(Name(g.Key), g.Select(a => new ActionCard(a)))).ToList();
    }

    public override string Title => "Speed up & fix";
    public override string Subtitle => "Pick a fix on the left. You'll see exactly what changes before anything happens.";

    public IReadOnlyList<CategoryGroup> Groups { get; }
    public ObservableCollection<OptionItem> Options { get; } = new();
    public ObservableCollection<SelectableItem> Items { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();
    [ObservableProperty] private ActionCard? _selected;
    [ObservableProperty] private ActionInspection? _inspection;
    [ObservableProperty] private bool _inspecting;
    [ObservableProperty] private string _optionParameter = "state";
    [ObservableProperty] private ChangeRecord? _lastRecord;
    private Dictionary<string, string> _extraParameters = new();

    public bool HasSelection => Selected is not null;
    public bool HasOptions => Options.Count > 0;
    public bool HasItems => Items.Count > 0;
    public bool Applicable => Inspection?.Applicable ?? false;
    public string StateText => Inspection is null ? "Checking…" : Inspection.Applicable ? Inspection.CurrentState : $"Not needed on this PC: {Inspection.NotApplicableReason}";
    public bool CanRollbackLast => LastRecord?.CanRollback == true;

    partial void OnSelectedChanged(ActionCard? oldValue, ActionCard? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(HasSelection));
        _ = InspectAsync();
    }
    partial void OnInspectionChanged(ActionInspection? value) { OnPropertyChanged(nameof(Applicable)); OnPropertyChanged(nameof(StateText)); }
    partial void OnLastRecordChanged(ChangeRecord? value) => OnPropertyChanged(nameof(CanRollbackLast));

    public async Task SelectActionAsync(string actionId, Dictionary<string, string>? parameters = null)
    {
        _extraParameters = parameters ?? new();
        var card = Groups.SelectMany(g => g.Actions).FirstOrDefault(a => a.Action.Id == actionId);
        if (card is null) { Dialogs.Toast($"Unknown action {actionId}", ToastKind.Warning); return; }
        if (Selected == card) await InspectAsync(); else Selected = card;
    }

    [RelayCommand]
    private void Select(ActionCard card) { _extraParameters = new(); Selected = card; }

    [RelayCommand]
    public async Task InspectAsync()
    {
        Options.Clear(); Items.Clear(); Notes.Clear();
        Inspection = null;
        if (Selected is null) return;
        Inspecting = true;
        try
        {
            var req = BuildRequest();
            var insp = await Task.Run(() => Selected.Action.InspectAsync(S.ActionContext, req, CancellationToken.None));
            OptionParameter = insp.OptionParameter;
            var preset = _extraParameters.TryGetValue(insp.OptionParameter, out var p) ? p : null;
            foreach (var o in insp.Options)
                Options.Add(new OptionItem { Value = o.Value, Label = o.Label, IsCurrent = o.IsCurrent, IsRecommended = o.IsRecommended, IsSelected = preset is not null ? o.Value == preset : o.IsRecommended && !o.IsCurrent });
            if (Options.Count > 0 && !Options.Any(o => o.IsSelected)) Options.First(o => !o.IsCurrent || Options.Count == 1).IsSelected = true;
            foreach (var i in insp.Items)
                Items.Add(new SelectableItem { Id = i.Id, Label = i.Label, Detail = i.Detail, State = i.CurrentState, Recommendation = i.Recommendation, Warning = i.Warning, Selectable = i.Selectable, IsSelected = i.Selected && i.Selectable });
            foreach (var n in insp.Notes) Notes.Add(n);
            Inspection = insp;
        }
        catch (Exception ex)
        {
            Inspection = ActionInspection.NotApplicable(ex.Message);
        }
        finally
        {
            Inspecting = false;
            OnPropertyChanged(nameof(HasOptions)); OnPropertyChanged(nameof(HasItems));
        }
    }

    [RelayCommand]
    private void ChooseOption(OptionItem item)
    {
        foreach (var o in Options) o.IsSelected = o == item;
        if (Selected?.Action.Id == Core.Diagnostics.ActionIds.ClearAppCache && OptionParameter == "app")
        {
            _extraParameters["app"] = item.Value;
            _ = InspectAsync();
        }
    }

    private ActionRequest BuildRequest()
    {
        var req = new ActionRequest { ActionId = Selected!.Action.Id, Parameters = new(_extraParameters) };
        var opt = Options.FirstOrDefault(o => o.IsSelected);
        if (opt is not null) req.Parameters[OptionParameter] = opt.Value;
        req.SelectedItems = Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        return req;
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (Selected is null || Inspection is null) return;
        if (!Inspection.Applicable) { Dialogs.Toast(StateText, ToastKind.Warning); return; }
        if (HasItems && !Items.Any(i => i.IsSelected) && Selected.Action.Id != Core.Diagnostics.ActionIds.VisualEffects)
        {
            Dialogs.Toast("Select at least one item first.", ToastKind.Warning);
            return;
        }
        var record = await Runner.RunAsync(BuildRequest());
        if (record is not null) LastRecord = record;
        await InspectAsync();
    }

    [RelayCommand]
    private async Task RollbackLast()
    {
        if (LastRecord is null) return;
        LastRecord = await Runner.RollbackAsync(LastRecord) ?? LastRecord;
        await InspectAsync();
    }

    [RelayCommand] private void SelectAllItems() { foreach (var i in Items.Where(i => i.Selectable)) i.IsSelected = true; }
    [RelayCommand] private void SelectNoItems() { foreach (var i in Items) i.IsSelected = false; }
}
