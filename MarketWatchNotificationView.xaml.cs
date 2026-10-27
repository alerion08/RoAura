using System.Windows;
using System.Windows.Controls;
using RoAura.Models;

namespace RoAura;

public partial class MarketWatchNotificationView : UserControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(nameof(Settings), typeof(MarketWatchNotificationSettings), typeof(MarketWatchNotificationView));
    public static readonly DependencyProperty NotificationProperty = DependencyProperty.Register(nameof(Notification), typeof(MarketWatchNotification), typeof(MarketWatchNotificationView));
    public MarketWatchNotificationSettings? Settings { get => (MarketWatchNotificationSettings?)GetValue(SettingsProperty); set => SetValue(SettingsProperty, value); }
    public MarketWatchNotification? Notification { get => (MarketWatchNotification?)GetValue(NotificationProperty); set => SetValue(NotificationProperty, value); }
    public MarketWatchNotificationView() => InitializeComponent();
}
