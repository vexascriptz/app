using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Optimization;

namespace SoftReset.App.ViewModels;

public sealed partial class SettingRow : ObservableObject
{
    public SettingRow(SettingDefinition d) => Definition = d;
    public SettingDefinition Definition { get; }
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    [ObservableProperty] private SettingState? _state;
    [ObservableProperty] private string _current = "…";
    public bool Unavailable => State is not null && State.Availability != SettingAvailability.Available;
    public bool Writable => Definition.Controls.Any(c => c.Type is "registry-set" or "registry-delete" or "action");
    public string Badges => Writable ? "Can change here" : "Opens in Windows";
    partial void OnStateChanged(SettingState? value) { Current = value?.Current.Display ?? "…"; OnPropertyChanged(nameof(Unavailable)); }
}

public sealed partial class SettingControlItem : ObservableObject
{
    private readonly SettingsExplorerViewModel _owner;
    public SettingControlItem(SettingsExplorerViewModel owner, SettingRow row, SettingControl control, int index) { _owner = owner; Row = row; Control = control; Index = index; }
    public SettingRow Row { get; }
    public SettingControl Control { get; }
    public int Index { get; }
    public string Label => Control.Label;
    public bool IsOpen => Control.Type == "open";
    public bool IsWrite => !IsOpen;
    [RelayCommand] private Task Execute() => _owner.ExecuteControlAsync(this);
}

public sealed partial class SettingsExplorerViewModel : PageViewModel
{
    private readonly List<SettingRow> _rows;
    private SettingsReader? _reader;

    public SettingsExplorerViewModel(Shell shell) : base(shell)
    {
        _rows = S.Knowledge.Settings.Select(d => new SettingRow(d)).ToList();
        Categories = new ObservableCollection<CategoryItem>(new[] { new CategoryItem("All settings", _rows.Count) }
            .Concat(_rows.GroupBy(r => r.Category).Select(g => new CategoryItem(g.Key, g.Count()))));
        _selectedCategory = Categories[0];
    }

    public override string Title => "Windows settings";
    public override string Subtitle => "Search for a setting to see what it does and change it.";

    public sealed record CategoryItem(string Name, int Count);
    public ObservableCollection<CategoryItem> Categories { get; }
    public ObservableCollection<SettingRow> Items { get; } = new();
    public ObservableCollection<SettingControlItem> Controls { get; } = new();
    [ObservableProperty] private CategoryItem _selectedCategory;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private SettingRow? _selected;
    [ObservableProperty] private bool _showUnavailable;
    public bool HasSelection => Selected is not null;

    partial void OnSelectedCategoryChanged(CategoryItem value) => Filter();
    partial void OnSearchChanged(string value) => Filter();
    partial void OnShowUnavailableChanged(bool value) => Filter();

    partial void OnSelectedChanged(SettingRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        Controls.Clear();
        if (value is null) return;
        if (value.State is null) value.State = Reader.Evaluate(value.Definition);
        for (var i = 0; i < value.Definition.Controls.Count; i++) Controls.Add(new SettingControlItem(this, value, value.Definition.Controls[i], i));
    }

    private SettingsReader Reader => _reader ??= new SettingsReader(S.Platform, S.Edition);

    public override async Task OnActivatedAsync()
    {
        Filter();
        await ReadVisibleAsync();
    }

    private void Filter()
    {
        var q = Search.Trim();
        Items.Clear();
        foreach (var r in _rows.Where(r => (SelectedCategory.Name == "All settings" || r.Category == SelectedCategory.Name)
                                          && (q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Definition.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                                              || r.Definition.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
                                          && (ShowUnavailable || !r.Unavailable)))
            Items.Add(r);
        _ = ReadVisibleAsync();
    }

    private async Task ReadVisibleAsync()
    {
        var pending = Items.Where(i => i.State is null).ToList();
        foreach (var row in pending)
        {
            var state = await Task.Run(() => Reader.Evaluate(row.Definition));
            row.State = state;
        }
        if (!ShowUnavailable && Items.Any(i => i.Unavailable))
        {
            foreach (var u in Items.Where(i => i.Unavailable).ToList()) Items.Remove(u);
        }
    }

    public async Task ExecuteControlAsync(SettingControlItem c)
    {
        var row = c.Row;
        if (row.Unavailable) { Dialogs.Toast(row.State?.AvailabilityNote ?? "Not available on this PC.", ToastKind.Warning); return; }
        switch (c.Control.Type)
        {
            case "open":
            {
                var t = c.Control.ToLaunchTarget()!;
                var r = S.Platform.Shell.Launch(t);
                if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
                else if (S.DemoMode) Dialogs.Toast(r.Message);
                break;
            }
            case "registry-set":
            case "registry-delete":
                await Runner.RunAsync(new ActionRequest { ActionId = ActionIds.SettingRegistrySet, Parameters = { ["setting"] = row.Definition.Id, ["control"] = c.Index.ToString() } });
                row.State = Reader.Evaluate(row.Definition);
                break;
            case "action":
            {
                var id = c.Control.ActionId!;
                if (RecommendationItem.NeedsSelection(id) || id is ActionIds.PowerPlan or ActionIds.RefreshRate && c.Control.Parameters is null)
                {
                    Shell.Main.Navigate(Shell.Main.Optimization);
                    await Shell.Main.Optimization.SelectActionAsync(id);
                }
                else
                {
                    await Runner.RunAsync(new ActionRequest { ActionId = id, Parameters = new(c.Control.Parameters ?? new()) });
                    row.State = Reader.Evaluate(row.Definition);
                }
                break;
            }
        }
    }

    [RelayCommand]
    private async Task RefreshValues()
    {
        foreach (var r in _rows) r.State = null;
        _reader = null;
        await ReadVisibleAsync();
        if (Selected is not null) Selected.State = Reader.Evaluate(Selected.Definition);
    }
}
