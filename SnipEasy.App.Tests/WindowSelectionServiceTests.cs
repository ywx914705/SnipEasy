using System.Drawing;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class WindowSelectionServiceTests
{
    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(299, 249, true)]
    [InlineData(300, 250, false)]
    [InlineData(99, 100, false)]
    public void WindowPixelBounds_Contains_UsesExclusiveRightAndBottom(
        int x,
        int y,
        bool expected)
    {
        var bounds = new WindowPixelBounds(100, 100, 300, 250);

        var result = bounds.Contains(new Point(x, y));

        Assert.Equal(expected, result);
        Assert.Equal(200, bounds.Width);
        Assert.Equal(150, bounds.Height);
    }
}
