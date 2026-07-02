using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

[Trait("Category", "Unit")]
public class SelectionManagerTests
{
    [Fact]
    public void HasValidSelection_NoSelection_ReturnsFalse()
    {
        var manager = new SelectionManager();
        Assert.False(manager.HasValidSelection);
    }

    [Fact]
    public void Selection_InitialState_HasZeroSize()
    {
        var manager = new SelectionManager();
        Assert.Equal(0, manager.Selection.Width);
        Assert.Equal(0, manager.Selection.Height);
    }
}
