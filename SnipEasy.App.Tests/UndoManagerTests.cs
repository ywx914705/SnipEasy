using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

[Trait("Category", "Unit")]
public class UndoManagerTests
{
    [Fact]
    public void CanUndo_EmptyStack_ReturnsFalse()
    {
        var manager = new UndoManager();
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsNull()
    {
        var manager = new UndoManager();
        var result = manager.Undo();
        Assert.Null(result);
    }

    [Fact]
    public void Clear_OnEmptyStack_DoesNotThrow()
    {
        var manager = new UndoManager();
        manager.Clear();
        Assert.False(manager.CanUndo);
    }
}
