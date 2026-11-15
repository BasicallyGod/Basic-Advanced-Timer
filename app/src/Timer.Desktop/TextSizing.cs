using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Layout;
using Avalonia.Threading;
using Microsoft.Win32;

namespace Timer.Desktop;

/// <summary>Windows' text preference changes typography, not DPI or the instrument geometry.</summary>
public sealed class TextSizing : AvaloniaObject
{
    public static readonly AttachedProperty<bool> FixedProperty = AvaloniaProperty.RegisterAttached<TextSizing, Control, bool>("Fixed", inherits: true);
    public static bool GetFixed(Control control) => control.GetValue(FixedProperty);
    public static void SetFixed(Control control, bool value) => control.SetValue(FixedProperty, value);
    private static readonly ConditionalWeakTable<Control, Font> fonts = new();
    private static readonly List<WeakReference<Font>> live = new();
    private static readonly ConditionalWeakTable<Window, WindowWatch> windows = new();
    private static bool installed, applying, native, pending;
    private static int registrations;
    public static double Factor { get; private set; } = 1;

    public static void UseWindowsPreference() { native = OperatingSystem.IsWindows(); if (native) Factor = ReadWindowsFactor(); }
    public static void Install()
    {
        if (installed) return; installed = true;
        TextElement.FontSizeProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (applying) return;
            // Template bindings already receive their owner's scaled size. Inherited/default sizes
            // likewise must never be multiplied again by every descendant.
            var own = change.Priority is not (BindingPriority.Inherited or BindingPriority.Template or BindingPriority.Unset);
            if (change.Priority == BindingPriority.Animation) return;
            if (!own && !fonts.TryGetValue(control, out _) && control is not (TextBox or ComboBox or Button)) return;
            if (!fonts.TryGetValue(control, out var font))
            {
                font = new Font(control); Register(control, font);
                if (native && control is Window window) windows.GetValue(window, w => new WindowWatch(w));
            }
            font.OwnSize = own;
            font.Size = change.GetNewValue<double>() / (!own && !GetFixed(control) ? Factor : 1); font.Apply();
        });
        FixedProperty.Changed.AddClassHandler<Control>((control, _) => { if (fonts.TryGetValue(control, out var font)) font.Apply(); });
        // Assigning a font equal to Avalonia's default raises no change event. Register it
        // when loaded as well; template/inherited values still belong to their owner.
        Control.LoadedEvent.AddClassHandler<Control>((control, args) =>
        {
            if (fonts.TryGetValue(control, out _)) return;
            var priority = control.GetDiagnostic(TextElement.FontSizeProperty).Priority;
            var own = priority is not (BindingPriority.Inherited or BindingPriority.Template or BindingPriority.Unset);
            if (!own && control is not (TextBox or ComboBox or Button)) return;
            var font = new Font(control) { OwnSize = own, Size = control.GetValue(TextElement.FontSizeProperty) / (!own && !GetFixed(control) ? Factor : 1) };
            Register(control, font); font.Apply();
        });
    }
    private static void Register(Control control, Font font)
    {
        fonts.Add(control, font); live.Add(new(font));
        // Virtualized/rebuilt lists must not accumulate dead weak-reference entries forever.
        if (++registrations % 1024 == 0) live.RemoveAll(reference => !reference.TryGetTarget(out _));
    }

    /// <summary>The unscaled design size, for a label which copies another label's typography.</summary>
    public static double BaseFontSize(Control control) => fonts.TryGetValue(control, out var font) ? font.Size : control.GetValue(TextElement.FontSizeProperty) / (GetFixed(control) ? 1 : Factor);
    public static double DecodeFactor(object? percent) => percent is int value && value is >= 100 and <= 225 ? value / 100.0 : 1;
    private static double ReadWindowsFactor()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility", writable: false); return DecodeFactor(key?.GetValue("TextScaleFactor")); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Factor; }
    }
    private static void Refresh()
    {
        pending = false; var next = ReadWindowsFactor(); if (next == Factor) return; SetFactor(next);
    }
    private static void SetFactor(double factor)
    {
        Factor = factor;
        for (var i = live.Count - 1; i >= 0; i--)
            if (live[i].TryGetTarget(out var font)) font.Apply(); else live.RemoveAt(i);
        Changed?.Invoke();
    }
    public static event Action? Changed;
    /// <summary>Update an existing layout without recreating editors or retaining detached pages.</summary>
    public static void Follow(Control owner, Action update)
    {
        var subscribed = false;
        owner.AttachedToVisualTree += (_, _) => { if (!subscribed) { Changed += update; subscribed = true; } update(); };
        owner.DetachedFromVisualTree += (_, _) => { if (subscribed) Changed -= update; subscribed = false; };
        update();
    }
    public static void Watch(Window window) { if (native) windows.GetValue(window, w => new WindowWatch(w)); }
    public static IDisposable ForTests(double factor)
    {
        Install();
        if (!double.IsFinite(factor) || factor is < 1 or > 2.25) throw new ArgumentOutOfRangeException(nameof(factor));
        var previous = Factor; SetFactor(factor); return new Restore(() => SetFactor(previous));
    }
    private sealed class Restore(Action restore) : IDisposable { private Action? action = restore; public void Dispose() { var current = action; action = null; current?.Invoke(); } }

    private sealed class WindowWatch
    {
        private readonly Window window;
        private bool hooked;
        public WindowWatch(Window window) { this.window = window; window.Opened += Opened; window.Activated += Activated; window.Closed += Closed; }
        private void Opened(object? sender, EventArgs e) { if (hooked) return; hooked = true; Win32Properties.AddWndProcHookCallback(window, Message); Refresh(); }
        private void Activated(object? sender, EventArgs e) => Refresh();
        private void Closed(object? sender, EventArgs e)
        {
            if (hooked) Win32Properties.RemoveWndProcHookCallback(window, Message);
            window.Opened -= Opened; window.Activated -= Activated; window.Closed -= Closed;
        }
        private nint Message(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x001A && !pending) { pending = true; Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background); }
            return 0; // Observe only: Avalonia still owns the message and its DPI handling.
        }
    }

    private sealed class Font
    {
        private readonly Control control;
        public double Size;
        public bool OwnSize;
        private double height, width;
        public Font(Control control)
        {
            this.control = control; height = control.Height; width = control.Width;
            control.PropertyChanged += (_, e) =>
            {
                if (applying) return;
                if (e.Property == Layoutable.HeightProperty) height = control.Height;
                else if (e.Property == Layoutable.WidthProperty) width = control.Width;
                else if (e.Property != ContentControl.ContentProperty && e.Property != TemplatedControl.PaddingProperty) return;
                Apply();
            };
            control.AttachedToVisualTree += (_, _) => Apply();
        }
        private bool TextControl => control is TextBox or ComboBox || control is Button b && HasText(b.Content);
        private static bool HasText(object? content) => content is string s && s.Length > 0 || content is TextBlock || content is Panel p && p.Children.Any(c => HasText(c is ContentControl cc ? cc.Content : c));
        public void Apply()
        {
            if (applying || Size <= 0) return;
            var factor = GetFixed(control) ? 1 : Factor;
            applying = true;
            try
            {
                if (OwnSize && control.GetValue(TextElement.FontSizeProperty) != Size * factor) control.SetCurrentValue(TextElement.FontSizeProperty, Size * factor);
                if (TextControl)
                {
                    var wantHeight = height + (factor - 1) * Size * 1.5;
                    if (double.IsFinite(height) && control.Height != wantHeight) control.SetCurrentValue(Layoutable.HeightProperty, wantHeight);
                    if (double.IsFinite(width) && control.Width != width * factor) control.SetCurrentValue(Layoutable.WidthProperty, width * factor);
                }
            }
            finally { applying = false; }
        }
    }
}
