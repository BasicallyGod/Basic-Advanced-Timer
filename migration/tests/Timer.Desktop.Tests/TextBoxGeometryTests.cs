using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TextBoxGeometryTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }

    [AvaloniaFact]
    public void FieldDecorationFitsShortNarrowAndMultilineBoxesBeforeAndAfterFocusAndResize()
    {
        var model = TimerModel.CreateDemo();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            model.SetTheme(theme);
            var fields = new[]
            {
                new TextBox { Text = "Regular", Width = 300, Height = 36 },
                new TextBox { Text = "00:30", Width = 58, Height = 36, MinWidth = 0 },
                new TextBox { Text = "Short", Width = 220, Height = 24, MinHeight = 0 },
                new TextBox { Text = "Two lines\nof text", Width = 300, Height = 70, AcceptsReturn = true },
                new TextBox { Text = "Custom corners", Width = 300, Height = 38, CornerRadius = new CornerRadius(6) },
                new TextBox { Text = "Borderless inline editor", Width = 300, Height = 30, MinHeight = 0, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Classes = { "bareField" } }
            };
            var column = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
            foreach (var field in fields) { field.HorizontalAlignment = HorizontalAlignment.Left; column.Children.Add(field); }
            var window = new Window { Width = 410, Height = 440, Background = Brush.Parse(model.CurrentTheme.Colour("Panel")), Content = column };
            window.Show(); Settle(window);
            try
            {
                void Check(TextBox field)
                {
                    var border = field.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
                    Assert.Equal(new Rect(field.Bounds.Size), new Rect(border.TranslatePoint(default, field)!.Value, border.Bounds.Size));
                    Assert.Equal(field.CornerRadius, border.CornerRadius);
                    Assert.Equal(field.BorderThickness, border.BorderThickness);
                    Assert.NotEqual(Color.Parse(model.CurrentTheme.Colour("Accent")), ((ISolidColorBrush)border.BorderBrush!).Color);
                    if (field.Classes.Contains("bareField")) Assert.Equal(0, border.BoxShadow.Count);
                    else { Assert.True(border.BoxShadow.Count > 0); Assert.All(border.BoxShadow, shadow => Assert.True(shadow.IsInset)); }
                }
                foreach (var field in fields)
                {
                    Check(field); var before = field.Bounds;
                    field.Focus(); Settle(window); Check(field); Assert.Equal(before, field.Bounds);
                    field.Width += 16; Settle(window); Check(field);
                }
                var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
                if (folder is not null)
                {
                    Directory.CreateDirectory(folder); fields[1].Focus(); Settle(window);
                    using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(folder, $"text-box-geometry-{theme}.png"));
                }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void InlineNoteEditorHasANeutralOuterFrameAndNoSecondFieldShadow()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddNote").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NoteField");
            field.Focus(); Settle(window);
            var frame = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("composer"));
            Assert.NotEqual(Color.Parse(model.CurrentTheme.Colour("Accent")), ((ISolidColorBrush)frame.BorderBrush!).Color);
            Assert.True(frame.BoxShadow.Count > 0); Assert.All(frame.BoxShadow, shadow => Assert.True(shadow.IsInset));
            var decoration = field.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Equal(0, decoration.BoxShadow.Count); Assert.Equal(new Thickness(0), decoration.BorderThickness);
        }
        finally { window.Close(); }
    }
}
