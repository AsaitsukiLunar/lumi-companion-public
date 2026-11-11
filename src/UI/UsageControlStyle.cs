// Version: 0.3.5-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: RAW | user manual-mode and styling correction; LOCAL-WORK | Codex / release_finish.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using Cursors = System.Windows.Input.Cursors;

namespace LumiCompanion.UI;

internal static class UsageControlStyle
{
    internal static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    internal static readonly Brush Surface = Brush(29, 32, 40);
    internal static readonly Brush Edge = Brush(66, 70, 82);
    internal static readonly Brush Muted = Brush(170, 177, 192);

    internal static readonly Style Button = MakeButtonStyle();
    internal static readonly Style MenuItem = MakeMenuItemStyle();
    private static Style MakeMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(220, 224, 233)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(MenuItem)) { VisualTree = border }));
        var hover = new Trigger { Property = System.Windows.Controls.MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Brush(51, 56, 68)));
        style.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
        style.Triggers.Add(disabled);
        return style;
    }
    private static Style MakeButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(202, 207, 220)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 2, 5, 2)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 22.0));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 22.0));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Brush(51, 56, 68)));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BackgroundProperty, Brush(51, 56, 68)));
        style.Triggers.Add(focus);
        var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, Brush(66, 72, 85)));
        style.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4));
        style.Triggers.Add(disabled);
        return style;
    }
}
