using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using RoAura.Models;
using Xunit;

namespace RoAura.Tests;

public sealed class LayoutInputTests
{
    private static readonly HashSet<string> LayoutProperties = new(StringComparer.Ordinal)
    {
        "IndividualIconSize", "IconSize", "IconPadding", "TextSize",
        "NotificationIconSize", "NotificationTextSize", "NotificationIconPadding",
        "Gap", "TimersGap", "TimersTextSize", "X", "Y", "NotificationX", "NotificationY",
        "TimersX", "TimersY", "ReadyOpacityPercent", "CooldownOpacityPercent", "OpacityPercent",
        "NotificationOpacityPercent", "ActiveOpacityPercent", "InactiveOpacityPercent"
    };

    [Fact]
    public void AllLayoutEditors_CommitAfterTyping()
    {
        var editors = ReadEditors().Where(e => LayoutProperties.Contains(BindingPath(e))).ToArray();
        Assert.NotEmpty(editors);
        foreach (var editor in editors)
            Assert.Contains("UpdateSourceTrigger=LostFocus", editor.Attribute("Text")!.Value);
    }

    // Use the actual XAML binding, not a hand-written copy: changing an editor
    // back to PropertyChanged must reproduce the user's 6 -> 20 -> 180 bug.
    [Theory]
    [InlineData("SkillAura", "IndividualIconSize", "64", 64d)]
    [InlineData("ReminderItem", "IconSize", "64", 64d)]
    [InlineData("ReminderItem", "TextSize", "24", 24d)]
    [InlineData("EquipmentTracker", "IconSize", "64", 64d)]
    [InlineData("EquipmentTracker", "TextSize", "24", 24d)]
    [InlineData("TrackedEffect", "NotificationIconSize", "64", 64d)]
    [InlineData("TrackedEffect", "NotificationTextSize", "24", 24d)]
    [InlineData("BuffPanelGroup", "IconSize", "64", 64d)]
    [InlineData("RateIndicatorSettings", "TextSize", "24", 24d)]
    [InlineData("RewardGroupSettings", "TextSize", "24", 24d)]
    [InlineData("AppSettings", "TimersTextSize", "24", 24d)]
    [InlineData("ReminderItem", "IconPadding", "0", 0d)]
    [InlineData("EquipmentTracker", "X", "-120", -120d)]
    [InlineData("EquipmentTracker", "Y", "120.5", 120.5d)]
    [InlineData("ReminderItem", "OpacityPercent", "50", 50d)]
    [InlineData("ReminderItem", "IconSize", "999", 180d)]
    [InlineData("ReminderItem", "TextSize", "1", 10d)]
    public void LayoutEditor_AllowsCompleteNumberBeforeClamping(
        string modelType, string propertyName, string input, double expected)
    {
        RunSta(() =>
        {
            var model = Activator.CreateInstance(typeof(ReminderItem).Assembly.GetType($"RoAura.Models.{modelType}")!)!;
            var property = model.GetType().GetProperty(propertyName)!;
            var initial = property.GetValue(model);
            // A path can occur in several feature editors; exercise each declaration.
            foreach (var declaration in ReadEditors().Where(e => BindingPath(e) == propertyName))
            {
                property.SetValue(model, initial);
                var editor = CreateEditor(declaration, model);
                SetText(editor, "");
                foreach (var character in input)
                {
                    var text = editor.Text + character;
                    SetText(editor, text);
                    Assert.Equal(text, editor.Text);
                    Assert.Equal(initial, property.GetValue(model));
                }

                editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, editor));
                DrainDispatcher();
                Assert.Equal(expected, Convert.ToDouble(property.GetValue(model), CultureInfo.InvariantCulture));
                Assert.False(Validation.GetHasError(editor));
            }
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void InvalidInput_KeepsPreviousValue_AndCanBeCorrected(string input)
    {
        RunSta(() =>
        {
            var model = new ReminderItem();
            var initial = model.IconSize;
            var declaration = ReadEditors().First(e => BindingPath(e) == "IconSize");
            var editor = CreateEditor(declaration, model);
            SetText(editor, input);
            editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, editor));
            DrainDispatcher();
            Assert.Equal(initial, model.IconSize);
            Assert.True(Validation.GetHasError(editor));

            SetText(editor, "64"); // Includes paste/replacement of the whole value.
            editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, editor));
            DrainDispatcher();
            Assert.Equal(64d, model.IconSize);
            Assert.False(Validation.GetHasError(editor));
        });
    }

    private static TextBox CreateEditor(XElement declaration, object model)
    {
        var element = new XElement(declaration.Name, new XAttribute("Text", declaration.Attribute("Text")!.Value));
        var editor = (TextBox)XamlReader.Parse(element.ToString());
        editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
        editor.DataContext = model;
        DrainDispatcher();
        Assert.NotNull(editor.GetBindingExpression(TextBox.TextProperty));
        return editor;
    }

    private static void SetText(TextBox editor, string text)
    {
        editor.SetCurrentValue(TextBox.TextProperty, text);
        DrainDispatcher();
    }

    private static void DrainDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static string BindingPath(XElement editor) =>
        editor.Attribute("Text")?.Value.Split(new[] { ' ', ',', '}' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault() ?? "";

    private static XElement[] ReadEditors()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "MainWindow.xaml");
            if (File.Exists(path))
                return XDocument.Load(path).Descendants()
                    .Where(e => e.Name.LocalName == "TextBox" && e.Attribute("Text") is not null).ToArray();
        }
        throw new FileNotFoundException("Could not locate MainWindow.xaml.");
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
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF input test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
