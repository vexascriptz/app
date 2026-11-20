using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SoftReset.Core.Models;
using SoftReset.Core.Parsing;
using SoftReset.Core.Safety;

namespace SoftReset.App;

public abstract class ViewModelBase : ObservableObject { }

/// <summary>Maps view models to views by naming convention (FooViewModel → FooView).</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Type?> Cache = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;
        var vmType = data.GetType();
        if (!Cache.TryGetValue(vmType, out var viewType))
        {
            var name = vmType.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            viewType = vmType.Assembly.GetType(name);
            Cache[vmType] = viewType;
        }
        return viewType is null ? new TextBlock { Text = $"No view for {vmType.Name}" } : (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}

public static class Converters
{
    private static IBrush B(string key) => Application.Current?.FindResource(key) as IBrush ?? Brushes.Gray;

    public static readonly IValueConverter SeverityForeground = new FuncValueConverter<Severity, IBrush>(s => s switch
    {
        Severity.Critical => B("DangerBrush"),
        Severity.High => B("HighBrush"),
        Severity.Medium => B("WarningBrush"),
        Severity.Low => B("AccentBrush"),
        _ => B("InfoBrush"),
    });

    public static readonly IValueConverter SeverityBackground = new FuncValueConverter<Severity, IBrush>(s => s switch
    {
        Severity.Critical => B("DangerBgBrush"),
        Severity.High => B("HighBgBrush"),
        Severity.Medium => B("WarningBgBrush"),
        Severity.Low => B("SkyLightBrush"),
        _ => B("InfoBgBrush"),
    });

    public static readonly IValueConverter StatusForeground = new FuncValueConverter<ChangeStatus, IBrush>(s => s switch
    {
        ChangeStatus.Verified or ChangeStatus.Applied => B("SuccessBrush"),
        ChangeStatus.RolledBack => B("AccentBrush"),
        ChangeStatus.Failed or ChangeStatus.RollbackFailed => B("DangerBrush"),
        ChangeStatus.Interrupted or ChangeStatus.VerificationFailed => B("WarningBrush"),
        _ => B("InfoBrush"),
    });

    public static readonly IValueConverter StatusBackground = new FuncValueConverter<ChangeStatus, IBrush>(s => s switch
    {
        ChangeStatus.Verified or ChangeStatus.Applied => B("SuccessBgBrush"),
        ChangeStatus.RolledBack => B("SkyLightBrush"),
        ChangeStatus.Failed or ChangeStatus.RollbackFailed => B("DangerBgBrush"),
        ChangeStatus.Interrupted or ChangeStatus.VerificationFailed => B("WarningBgBrush"),
        _ => B("InfoBgBrush"),
    });

    public static readonly IValueConverter Bytes = new FuncValueConverter<long?, string>(b => ToolOutputParsers.FormatBytes(b));
    public static readonly IValueConverter BytesL = new FuncValueConverter<long, string>(b => ToolOutputParsers.FormatBytes(b));
    public static readonly IValueConverter Percent = new FuncValueConverter<double, string>(v => $"{v:0.#}%");
    public static readonly IValueConverter NotNull = new FuncValueConverter<object?, bool>(o => o is not null);
    public static readonly IValueConverter IsNull = new FuncValueConverter<object?, bool>(o => o is null);
    public static readonly IValueConverter NotEmpty = new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));
    public static readonly IValueConverter Not = new FuncValueConverter<bool, bool>(b => !b);
    public static readonly IValueConverter Upper = new FuncValueConverter<object?, string>(o => o?.ToString()?.ToUpperInvariant() ?? "");
    public static readonly IValueConverter LocalTime = new FuncValueConverter<DateTimeOffset, string>(d => d.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
    public static readonly IValueConverter LocalTimeN = new FuncValueConverter<DateTimeOffset?, string>(d => d is { } v ? v.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : "—");
    public static readonly IValueConverter ToastIsSuccess = new FuncValueConverter<ViewModels.ToastKind, bool>(k => k == ViewModels.ToastKind.Success);
    public static readonly IValueConverter ToastIsWarning = new FuncValueConverter<ViewModels.ToastKind, bool>(k => k is ViewModels.ToastKind.Warning or ViewModels.ToastKind.Error);
    public static readonly IValueConverter YesNo = new FuncValueConverter<bool, string>(b => b ? "Yes" : "No");
    public static readonly IValueConverter AssessmentBrush = new FuncValueConverter<string, IBrush>(a => a switch
    {
        "Improved" => B("SuccessBrush"),
        "Worse" => B("DangerBrush"),
        _ => B("MutedBrush"),
    });
    public static readonly IValueConverter HealthBrush = new FuncValueConverter<int, IBrush>(v => v >= 80 ? B("SuccessBrush") : v >= 55 ? B("WarningBrush") : B("DangerBrush"));
}

/// <summary>A minimal line chart for live CPU/memory history.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.DodgerBlue);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), 100);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, FillProperty, MaximumProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var v = Values;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (v is null || v.Count < 2 || w <= 0 || h <= 0) return;
        var step = w / (v.Count - 1);
        var pts = v.Select((y, i) => new Point(i * step, h - Math.Clamp(y / Maximum, 0, 1) * (h - 2) - 1)).ToList();
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false);
            foreach (var p in pts.Skip(1)) g.LineTo(p);
            g.EndFigure(false);
        }
        if (Fill is not null)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(0, h), true);
                foreach (var p in pts) g.LineTo(p);
                g.LineTo(new Point(w, h));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(Fill, null, area);
        }
        ctx.DrawGeometry(null, new Pen(Stroke, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}

/// <summary>Circular health score gauge.</summary>
public sealed class HealthRing : Control
{
    public static readonly StyledProperty<int> ValueProperty = AvaloniaProperty.Register<HealthRing, int>(nameof(Value));
    static HealthRing() => AffectsRender<HealthRing>(ValueProperty);
    public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var thickness = size * 0.1;
        var r = (size - thickness) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#E3EEFB")), thickness), c, r, r);
        var frac = Math.Clamp(Value / 100.0, 0, 1);
        if (frac > 0)
        {
            var color = Value >= 80 ? Color.Parse("#1E8E5A") : Value >= 55 ? Color.Parse("#D69A1A") : Color.Parse("#C62828");
            var start = -Math.PI / 2;
            var end = start + frac * Math.PI * 2 - 0.0001;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(c.X + r * Math.Cos(start), c.Y + r * Math.Sin(start)), false);
                g.ArcTo(new Point(c.X + r * Math.Cos(end), c.Y + r * Math.Sin(end)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round), geo);
        }
        var text = new FormattedText(Value.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("fonts:Inter#Inter, $Default"), FontStyle.Normal, FontWeight.SemiBold), size * 0.3, new SolidColorBrush(Color.Parse("#0B2545")));
        ctx.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
    }
}
