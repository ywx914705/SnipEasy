namespace SnipEasy.App.Tests;

public class MainWindowCapturePolicyTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRestoreMainWindowAfterCapture_CopyCompleted_ReturnsFalse()
    {
        var shouldRestore = MainWindow.ShouldRestoreMainWindowAfterCapture(
            wasVisibleBeforeCapture: true,
            copiedToClipboard: true);

        Assert.False(shouldRestore);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRestoreMainWindowAfterCapture_CaptureCancelled_ReturnsTrue()
    {
        var shouldRestore = MainWindow.ShouldRestoreMainWindowAfterCapture(
            wasVisibleBeforeCapture: true,
            copiedToClipboard: false);

        Assert.True(shouldRestore);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ShouldRestoreMainWindowAfterCapture_WindowWasHidden_ReturnsFalse()
    {
        var shouldRestore = MainWindow.ShouldRestoreMainWindowAfterCapture(
            wasVisibleBeforeCapture: false,
            copiedToClipboard: false);

        Assert.False(shouldRestore);
    }
}
