using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using RoAura.Controls;
using Xunit;

namespace RoAura.Tests;

public sealed class CommonControlStyleTests
{
    [Fact]
    public void DropdownArrowRetainsItsColorAndHasNoButtonChromeWhenOpen()
    {
        RunSta(() =>
        {
            var theme = Theme();
            var combo = new ComboBox { Width = 300, IsEditable = true, Style = (Style)theme[typeof(ComboBox)] };
            combo.Items.Add("First"); combo.Items.Add("Second"); combo.SelectedIndex = 0;
            var window = new Window { Content = combo, SizeToContent = SizeToContent.WidthAndHeight };
            try
            {
                window.Show(); combo.ApplyTemplate(); window.UpdateLayout();
                var arrow = (ToggleButton)combo.Template.FindName("DropButton", combo);
                arrow.ApplyTemplate();
                var path = Descendants<System.Windows.Shapes.Path>(arrow).Single();
                var brush = path.Stroke;
                Assert.Equal(new Thickness(0), arrow.BorderThickness);
                Assert.DoesNotContain(Descendants<Border>(arrow), b => b.BorderThickness != new Thickness(0));
                combo.IsDropDownOpen = true; window.UpdateLayout();
                Assert.Same(brush, path.Stroke);
                combo.IsDropDownOpen = false;
                combo.SelectedIndex = 1; window.UpdateLayout();
                Assert.Equal("Second", ((TextBox)combo.Template.FindName("PART_EditableTextBox", combo)).Text);
                Assert.False(combo.IsDropDownOpen);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NumericAndColorFieldsShareTheRoundedTextTemplate_AndSliderUsesRoundThumb()
    {
        RunSta(() =>
        {
            var theme = Theme();
            var template = ((Style)theme["RoTextBoxStyle"]).Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
            foreach (var editor in new TextBox[] { new TextBox(), new NumericTextBox(), new ColorTextBox() })
            {
                editor.Template = (ControlTemplate)template; editor.ApplyTemplate();
                Assert.Equal(new CornerRadius(5), ((Border)editor.Template.FindName("TextBorder", editor)).CornerRadius);
            }
            var slider = new Slider { Style = (Style)theme[typeof(Slider)] };
            slider.ApplyTemplate();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            track.Thumb.ApplyTemplate();
            Assert.Equal(new CornerRadius(8), Descendants<Border>(track.Thumb).Single().CornerRadius);
        });
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void CompactFieldsHaveRoomForTheWholeTextLine(double scale)
    {
        RunSta(() =>
        {
            var theme = Theme();
            var panel = new StackPanel { LayoutTransform = new ScaleTransform(scale, scale) };
            var window = new Window { Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
                Style = (Style)theme[typeof(Window)] };
            window.Resources.MergedDictionaries.Add(theme);
            var editors = new TextBox[]
            {
                new TextBox { Text = "Новый скилл" },
                new TextBox { Text = "Skill name" },
                new NumericTextBox { Value = 12 },
                new NumericTextBox { Value = 100 },
                new ColorTextBox { Text = "#FF6FD8FF" }
            };
            try
            {
                foreach (var editor in editors)
                {
                    editor.Style = (Style)theme["RoTextBoxStyle"];
                    editor.Width = 200;
                    editor.Height = 28;
                    editor.VerticalContentAlignment = VerticalAlignment.Center;
                    panel.Children.Add(editor);
                }
                window.Show(); window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                foreach (var editor in editors)
                {
                    var host = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
                    Assert.Equal(new Thickness(0), host.Margin);
                    Assert.True(editor.ViewportHeight >= editor.ExtentHeight - 0.5,
                        $"Text '{editor.Text}' at {scale}: viewport {editor.ViewportHeight}, extent {editor.ExtentHeight}");
                    var first = editor.GetRectFromCharacterIndex(0);
                    Assert.False(first.IsEmpty);
                    Assert.True(first.Top >= 1 - 0.5 && first.Bottom <= editor.ActualHeight - 1 + 0.5,
                        $"Text '{editor.Text}' is clipped: {first}, field height {editor.ActualHeight}");
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CompactTableTextIsVerticallyCenteredInItsRow()
    {
        RunSta(() =>
        {
            // Use the actual per-column style from the Database settings screen.
            var path = FindRepositoryFile("DatabaseSettingsControl.xaml");
            var doc = System.Xml.Linq.XDocument.Load(path);
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var element = doc.Descendants(ns + "Style").Single(e => (string?)e.Attribute(x + "Key") == "WatchTableText");
            var context = new System.Windows.Markup.ParserContext();
            context.XmlnsDictionary.Add("", ns.NamespaceName);
            context.XmlnsDictionary.Add("x", x.NamespaceName);
            // Base style supplies only foreground; the setters under test define row placement.
            element.Attribute("BasedOn")?.Remove();
            var style = (Style)System.Windows.Markup.XamlReader.Parse(element.ToString(), context);
            var text = new TextBlock { Text = "Jur [3] · #1251", Style = style, FontSize = 12 };
            var row = new Grid { Height = 26, Width = 300 };
            row.Children.Add(text);
            row.Measure(new Size(300, 26)); row.Arrange(new Rect(0, 0, 300, 26)); row.UpdateLayout();
            var y = text.TranslatePoint(new Point(), row).Y;
            Assert.Equal((26 - text.ActualHeight) / 2, y, 1);
            Assert.True(y > 0);
            foreach (var column in doc.Descendants(ns + "DataGridTextColumn"))
                Assert.Equal("{StaticResource WatchTableText}", (string?)column.Attribute("ElementStyle"));
        });
    }

    private static string FindRepositoryFile(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "RoAura.csproj"))) return candidate;
        }
        throw new FileNotFoundException(name);
    }

    private static ResourceDictionary Theme() => (ResourceDictionary)Application.LoadComponent(
        new Uri("/RoAura;component/Themes/RagnarokTheme.xaml", UriKind.Relative));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF style test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
