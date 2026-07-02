using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class FfmpegRecordingServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void BuildArguments_NoAudio_IncludesAnFlag()
    {
        // Arrange
        var settings = new AppSettings
        {
            RecordingFrameRate = 12,
            RecordingCrf = 23,
            RecordingPerformanceMode = RecordingPerformanceProfiles.Balanced
        };

        // Act
        var args = FfmpegRecordingService.BuildArguments(settings, "out.mp4");

        // Assert
        Assert.Contains("-an", args);
        Assert.Contains("libx264", args);
        Assert.Contains("-crf", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildArguments_MixedAudio_IncludesAmixFilter()
    {
        // Arrange
        var settings = new AppSettings
        {
            RecordingFrameRate = 12,
            RecordingCrf = 23,
            RecordingPerformanceMode = RecordingPerformanceProfiles.Balanced,
            RecordingCaptureSystemAudio = true,
            RecordingSystemAudioDevice = "Stereo Mix",
            RecordingCaptureMicrophone = true,
            RecordingMicrophoneDevice = "Microphone"
        };

        // Act
        var args = FfmpegRecordingService.BuildArguments(settings, "out.mp4");

        // Assert
        Assert.Contains("[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[aout]", args);
        Assert.Contains("audio=Stereo Mix", args);
        Assert.Contains("audio=Microphone", args);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildArguments_SystemAudioOnly_MapsAudio()
    {
        // Arrange
        var settings = new AppSettings
        {
            RecordingFrameRate = 12,
            RecordingCrf = 23,
            RecordingPerformanceMode = RecordingPerformanceProfiles.Balanced,
            RecordingCaptureSystemAudio = true,
            RecordingSystemAudioDevice = "virtual-audio-capturer"
        };

        // Act
        var args = FfmpegRecordingService.BuildArguments(settings, "out.mp4");

        // Assert
        Assert.Contains("audio=virtual-audio-capturer", args);
        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseAudioDevices_ValidOutput_ExtractsDevices()
    {
        // Arrange
        var output = """
            [dshow @ 000] DirectShow video devices (some may be both video and audio devices)
            [dshow @ 000]  "Camera"
            [dshow @ 000] DirectShow audio devices
            [dshow @ 000]  "Microphone Array"
            [dshow @ 000]  "virtual-audio-capturer"
            """;

        // Act
        var devices = FfmpegRecordingService.ParseAudioDevices(output);

        // Assert
        Assert.Equal(2, devices.Count);
        Assert.Equal("Microphone Array", devices[0]);
        Assert.Equal("virtual-audio-capturer", devices[1]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseAudioDevices_NoAudioSection_ReturnsEmpty()
    {
        // Arrange
        var output = "[dshow @ 000] DirectShow video devices";

        // Act
        var devices = FfmpegRecordingService.ParseAudioDevices(output);

        // Assert
        Assert.Empty(devices);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildMissingFfmpegMessage_ContainsRequiredInfo()
    {
        // Act
        var message = FfmpegRecordingService.BuildMissingFfmpegMessage();

        // Assert
        Assert.Contains("录制电脑声音", message);
        Assert.Contains("ffmpeg.exe", message);
        Assert.Contains(Path.Combine("tools", "ffmpeg", "ffmpeg.exe"), message);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Could not find audio only device with name [bad]", "音频设备名称无效")]
    [InlineData("Permission denied", "写入权限")]
    [InlineData("Unknown encoder 'libx264'", "缺少所需编码器")]
    [InlineData("Unknown input format: gdigrab", "不支持 Windows 屏幕采集")]
    [InlineData("No such file or directory", "无法访问输入设备或输出路径")]
    public void BuildFriendlyError_KnownErrors_ReturnsFriendlyMessage(string stderr, string expectedFragment)
    {
        // Act
        var result = FfmpegRecordingService.BuildFriendlyError(stderr);

        // Assert
        Assert.Contains(expectedFragment, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildFriendlyError_UnknownError_ReturnsEmpty()
    {
        // Act
        var result = FfmpegRecordingService.BuildFriendlyError("some unknown error");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildFriendlyError_EmptyInput_ReturnsEmpty()
    {
        // Act
        var result = FfmpegRecordingService.BuildFriendlyError("");

        // Assert
        Assert.Equal("", result);
    }
}
