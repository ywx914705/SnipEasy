using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class LocalAviRecordingServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task PauseAsync_WhenNotRecording_Throws()
    {
        using var service = new LocalAviRecordingService();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PauseAsync());

        Assert.Contains("没有正在进行", error.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResumeAsync_WhenNotRecording_Throws()
    {
        using var service = new LocalAviRecordingService();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync());

        Assert.Contains("没有正在进行", error.Message);
    }
}
