using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using RoAura.Models;
using RoAura.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        {
            Assert.Equal("NumericTextBox", editor.Name.LocalName);
            Assert.Null(editor.Attribute("Text"));
            Assert.NotNull(editor.Attribute("Value"));
        }
    }

    // Use the actual editor and model binding from the view.
    [Theory]
    [InlineData("MarketWatchNotificationSettings", "SoundVolumePercent", "41.798", 42d)]
    [InlineData("MarketWatchNotificationSettings", "SoundVolumePercent", "50", 50d)]
    [InlineData("MarketWatchNotificationSettings", "IconSize", "64", 64d)]
    [InlineData("MarketWatchNotificationSettings", "TextSize", "24", 24d)]
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
    [InlineData("EquipmentTracker", "Y", "120,5", 120.5d)]
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

                editor.CommitInput();
                DrainDispatcher();
                Assert.Equal(expected, Convert.ToDouble(property.GetValue(model), CultureInfo.InvariantCulture));
                Assert.Equal(expected.ToString("G", CultureInfo.CurrentCulture), editor.Text);
                Assert.False(Validation.GetHasError(editor));
            }
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void InvalidInput_KeepsPreviousValue_AndCanBeCorrected(string input)
    {
        RunSta(() =>
        {
            var model = new ReminderItem();
            var initial = model.IconSize;
            var declaration = ReadEditors().First(e => BindingPath(e) == "IconSize");
            var editor = CreateEditor(declaration, model);
            SetText(editor, input);
            editor.CommitInput();
            DrainDispatcher();
            Assert.Equal(initial, model.IconSize);
            Assert.True(Validation.GetHasError(editor));

            SetText(editor, "64"); // Includes paste/replacement of the whole value.
            editor.CommitInput();
            DrainDispatcher();
            Assert.Equal(64d, model.IconSize);
            Assert.False(Validation.GetHasError(editor));
        });
    }

    [Fact]
    public void SourceNotification_DoesNotReplaceDraftOrCaret()
    {
        RunSta(() =>
        {
            var model = new ReminderItem();
            var editor = CreateEditor(ReadEditors().First(e => BindingPath(e) == "IconSize"), model);
            editor.SelectAll();
            editor.SelectedText = "6";
            editor.CaretIndex = 1;
            model.IconSize = 180;
            DrainDispatcher();
            Assert.Equal("6", editor.Text);
            Assert.Equal(1, editor.CaretIndex);
            editor.SelectedText = "4";
            Assert.Equal("64", editor.Text);
            Assert.True(editor.CommitInput());
            Assert.Equal(64d, model.IconSize);
            Assert.Equal("64", editor.Text);

            model.IconSize = 80;
            DrainDispatcher();
            Assert.Equal("80", editor.Text); // Slider/source updates still work when idle.

            model.IconSize = 180;
            DrainDispatcher();
            SetText(editor, "999");
            Assert.True(editor.CommitInput());
            Assert.Equal(180d, model.IconSize);
            Assert.Equal("180", editor.Text); // Setter raised no change event at the same bound.
        });
    }

    [Fact]
    public void VisibleEditor_CommitsOnEnterAndFocusChange_AndCancelsOnEscape()
    {
        RunSta(() =>
        {
            var model = new ReminderItem { IconSize = 80 };
            var editor = CreateEditor(ReadEditors().First(e => BindingPath(e) == "IconSize"), model);
            var next = new TextBox();
            var panel = new StackPanel();
            panel.Children.Add(editor);
            panel.Children.Add(next);
            var window = new Window { Content = panel, Width = 300, Height = 140 };
            try
            {
                window.Show();
                editor.Focus();
                editor.SelectAll();
                editor.SelectedText = "6";
                editor.CaretIndex = editor.Text.Length;
                editor.SelectedText = "4";
                Assert.Equal("64", editor.Text);
                Assert.Equal(80d, model.IconSize); // Still uncommitted.
                SendKey(editor, Key.Enter);
                Assert.Equal(64d, model.IconSize);

                editor.SelectAll();
                editor.SelectedText = "9";
                editor.CaretIndex = editor.Text.Length;
                editor.SelectedText = "6";
                Assert.Equal(64d, model.IconSize);
                next.Focus();
                DrainDispatcher();
                Assert.Equal(96d, model.IconSize);

                editor.Focus();
                editor.SelectAll();
                editor.SelectedText = "12";
                SendKey(editor, Key.Escape);
                Assert.Equal("96", editor.Text);
                Assert.Equal(96d, model.IconSize);
            }
            finally { window.Close(); }
        });
    }

    private static void SendKey(NumericTextBox editor, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        editor.RaiseEvent(e);
        DrainDispatcher();
        Assert.True(e.Handled);
    }

    private static NumericTextBox CreateEditor(XElement declaration, object model)
    {
        XNamespace controls = "clr-namespace:RoAura.Controls;assembly=RoAura";
        var element = new XElement(controls + "NumericTextBox",
            new XAttribute(XNamespace.Xmlns + "controls", controls.NamespaceName),
            new XAttribute("xmlns", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
            new XAttribute("Value", declaration.Attribute("Value")!.Value));
        var editor = (NumericTextBox)XamlReader.Parse(element.ToString());
        editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
        editor.DataContext = model;
        DrainDispatcher();
        Assert.NotNull(editor.GetBindingExpression(NumericTextBox.ValueProperty));
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
        (editor.Attribute("Value") ?? editor.Attribute("Text"))?.Value.Split(new[] { ' ', ',', '}' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault() ?? "";

    private static XElement[] ReadEditors()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "MainWindow.xaml");
            if (File.Exists(path))
                return new[] { path, Path.Combine(directory.FullName, "DatabaseSettingsControl.xaml") }
                    .SelectMany(file => XDocument.Load(file).Descendants())
                    .Where(e => e.Name.LocalName is "TextBox" or "NumericTextBox").ToArray();
        }
        throw new FileNotFoundException("Could not locate MainWindow.xaml.");
    }

    [Fact]
    public void WatchSearch_DoesNotSelectOrReplaceTheTypingDraft()
    {
        RunSta(() =>
        {
            var database = new RoAura.Services.GameDatabaseService();
            Assert.NotEmpty(database.Items);
            var dialog = new MarketWatchDialog(database);
            try
            {
                var editor = (MarketWatchEditor)dialog.FindName("Editor");
                var search = (TextBox)editor.FindName("ItemSearch");
                var suggestions = (ListBox)editor.FindName("Suggestions");
                var popup = (System.Windows.Controls.Primitives.Popup)editor.FindName("SuggestionsPopup");
                dialog.Show();
                search.Focus();
                DrainDispatcher();
                var name = database.Items.First(i => i.Name.Length > 2).Name;
                var draft = "";
                foreach (var character in name.Take(3))
                {
                    draft += character;
                    search.Text = draft;
                    search.CaretIndex = search.Text.Length;
                    DrainDispatcher();
                    Assert.Equal(draft, search.Text);
                    Assert.Equal(0, search.SelectionLength);
                    Assert.Equal(-1, suggestions.SelectedIndex);
                }
                Assert.NotEmpty(suggestions.Items.Cast<object>());
                Assert.True(popup.IsOpen);
                Assert.True(search.IsKeyboardFocusWithin);
                suggestions.SelectedIndex = 0;
                Assert.Equal(draft, search.Text);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void WatchStarDialog_ConfirmsTheExactPassedItemWithoutLookingUpItsName()
    {
        RunSta(() =>
        {
            // Unknown/custom item names can still be watched from a live market row.
            var dialog = new MarketWatchDialog(new RoAura.Services.GameDatabaseService(),
                new DatabaseItem { Id = 900001, Name = "Custom offer name" });
            Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                ((CheckBox)((MarketWatchEditor)dialog.FindName("Editor")).FindName("WithoutPrice")).IsChecked = true;
                ((Button)dialog.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.ApplicationIdle);
            Assert.True(dialog.ShowDialog());
            Assert.NotNull(dialog.Rule);
            Assert.Equal(900001, dialog.Rule.ItemId);
            Assert.True(dialog.Rule.WithoutPrice);
        });
    }

    [Fact]
    public void SharedNotificationViewHidesIconWithoutReservingItsWidth()
    {
        RunSta(() =>
        {
            var config = new MarketWatchNotificationSettings { ShowIcon = true, IconSize = 40, Padding = 0 };
            var view = new MarketWatchNotificationView { Settings = config, Notification = new("Diamond", null, DateTime.MaxValue) };
            DrainDispatcher();
            view.Measure(new Size(1000, 1000));
            var withIcon = view.DesiredSize.Width;
            config.ShowIcon = false;
            DrainDispatcher();
            view.Measure(new Size(1000, 1000));
            Assert.True(view.DesiredSize.Width < withIcon);
        });
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
