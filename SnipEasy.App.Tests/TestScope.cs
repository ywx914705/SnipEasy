namespace SnipEasy.App.Tests;

/// <summary>
/// Provides a temporary directory for tests that is automatically cleaned up on disposal.
/// </summary>
internal sealed class TestScope : IDisposable
{
    private TestScope(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public static TestScope Create()
    {
        return new TestScope(Path.Combine(Path.GetTempPath(), $"SnipEasyTests_{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best effort cleanup - some files may be locked
            }
        }
    }
}
