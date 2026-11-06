using System.Windows;
using RoAura.Services;

namespace RoAura;

public partial class EffectTutorialWindow : Window
{
    internal string VideoPath { get; }

    internal bool PlaybackFailed { get; private set; }

    internal EffectTutorialWindow(string videoFileName, string titleResourceKey)
    {
        InitializeComponent();
        VideoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tutorials", videoFileName);
        SetResourceReference(TitleProperty, titleResourceKey);
        Loaded += StartPlayback;
    }

    private void StartPlayback(object sender, RoutedEventArgs e)
    {
        Loaded -= StartPlayback;
        if (!File.Exists(VideoPath))
        {
            ShowPlaybackError();
            return;
        }

        TutorialVideo.Source = new Uri(VideoPath, UriKind.Absolute);
        TutorialVideo.Play();
    }

    private void TutorialVideo_MediaOpened(object sender, RoutedEventArgs e) =>
        PlaybackStatus.Visibility = Visibility.Collapsed;

    private void TutorialVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        TutorialVideo.Position = TimeSpan.Zero;
        TutorialVideo.Play();
    }

    private void TutorialVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DebugLog.Write($"EFFECT TUTORIAL PLAYBACK FAILED ({Path.GetFileName(VideoPath)}): {e.ErrorException.Message}");
        ShowPlaybackError();
    }

    private void ShowPlaybackError()
    {
        PlaybackFailed = true;
        PlaybackStatus.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "EffectTutorialUnavailable");
        PlaybackStatus.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        TutorialVideo.Close();
        base.OnClosed(e);
    }
}
