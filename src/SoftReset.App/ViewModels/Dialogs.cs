using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Optimization;
using SoftReset.Core.Safety;

namespace SoftReset.App.ViewModels;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue", string cancelText = "Cancel", bool danger = false, string? requireTypedText = null);
    Task<bool> ApproveAsync(ApprovalDialogViewModel dialog);
    Task ShowMessageAsync(string title, string message);
    Task<bool> KeepDisplayChangesAsync(int seconds);
    void Toast(string text, ToastKind kind = ToastKind.Info);
    IDisposable Busy(string text);
}

public enum ToastKind { Info, Success, Warning, Error }

public abstract partial class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    public Task<bool> Result => _tcs.Task;
    public event EventHandler? Closed;

    protected void Close(bool result)
    {
        if (_tcs.TrySetResult(result)) Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public virtual void Cancel() => Close(false);
}

public sealed partial class ConfirmDialogViewModel : DialogViewModel
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string ConfirmText { get; init; } = "Continue";
    public string CancelText { get; init; } = "Cancel";
    public bool Danger { get; init; }
    public string? RequireTypedText { get; init; }
    public bool HasTypedRequirement => RequireTypedText is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _typedText = "";

    private bool CanConfirm() => RequireTypedText is null || string.Equals(TypedText.Trim(), RequireTypedText, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    public void Confirm() => Close(true);
}

public sealed partial class MessageDialogViewModel : DialogViewModel
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";

    [RelayCommand]
    public void Ok() => Close(true);
}

/// <summary>Per-action approval: shows exactly what will change, the risks, and whether it can be undone.</summary>
public sealed partial class ApprovalDialogViewModel : DialogViewModel
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ExpectedBenefit { get; init; } = "";
    public string Risks { get; init; } = "";
    public bool RequiresAdmin { get; init; }
    public bool RequiresRestart { get; init; }
    public Reversibility Reversibility { get; init; }
    public long? EstimatedBytesFreed { get; init; }
    public ObservableCollection<PreviewChange> Changes { get; init; } = new();
    public ObservableCollection<string> Warnings { get; init; } = new();

    public string ReversibilityText => Reversibility switch
    {
        Reversibility.Full => "Can be undone",
        Reversibility.Partial => "Can be partly undone",
        _ => "Can't be undone",
    };
    public bool IsIrreversible => Reversibility == Reversibility.None;
    public string AdminText => RequiresAdmin ? "Windows will ask for permission" : "";
    public bool HasAdminText => RequiresAdmin;
    public bool HasWarnings => Warnings.Count > 0;
    public string? FreedText => EstimatedBytesFreed is > 0 ? $"Frees about {Core.Parsing.ToolOutputParsers.FormatBytes(EstimatedBytesFreed)}" : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand))]
    private bool _customerApproved;

    [RelayCommand(CanExecute = nameof(CustomerApproved))]
    public void Approve() => Close(true);
}

public sealed partial class KeepChangesDialogViewModel : DialogViewModel
{
    private readonly DispatcherTimer _timer;

    public KeepChangesDialogViewModel(int seconds)
    {
        SecondsLeft = seconds;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            SecondsLeft--;
            if (SecondsLeft <= 0) { _timer.Stop(); Close(false); }
        };
        _timer.Start();
    }

    [ObservableProperty] private int _secondsLeft;

    [RelayCommand]
    public void Keep() { _timer.Stop(); Close(true); }

    public override void Cancel() { _timer.Stop(); base.Cancel(); }
}

public sealed partial class ToastItem : ObservableObject
{
    public string Text { get; init; } = "";
    public ToastKind Kind { get; init; }
}
