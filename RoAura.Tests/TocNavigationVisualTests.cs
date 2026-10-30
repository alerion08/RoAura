using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoAura.Converters;
using Xunit;

namespace RoAura.Tests;

[Collection("WPF UI")]
public sealed class TocNavigationVisualTests
{
    [Fact]
    public void DisabledIconsAreGrayAndPreserveTransparency()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                byte[] input = [20, 40, 200, 255, 200, 30, 40, 0, 20, 220, 30, 128];
                var source = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, input, 12);
                var gray = NavigationIconConverter.ToGrayscale(source);
                var output = new byte[12];
                gray.CopyPixels(output, 12, 0);
                for (var i = 0; i < 12; i += 4)
                {
                    Assert.Equal(output[i], output[i + 1]);
                    Assert.Equal(output[i], output[i + 2]);
                    Assert.Equal(input[i + 3], output[i + 3]);
                }
                Assert.True(gray.IsFrozen);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
