using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SnipEasy.App.Services;

public sealed class UpdateCheckService
{
    private readonly string _manifestPath;

    public UpdateCheckService(string manifestPath)
    {
        _manifestPath = manifestPath;
    }

    public UpdateCheckResult Check(Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(_manifestPath) || !File.Exists(_manifestPath))
        {
            return new UpdateCheckResult(false, currentVersion.ToString(), "", "未配置本地更新清单。");
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(_manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return new UpdateCheckResult(false, currentVersion.ToString(), "", "更新清单无效。");
            }

            var latest = Version.Parse(manifest.Version);
            return latest > currentVersion
                ? new UpdateCheckResult(true, latest.ToString(), manifest.DownloadPath, manifest.Notes)
                : new UpdateCheckResult(false, latest.ToString(), manifest.DownloadPath, "当前已是最新版本。");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, currentVersion.ToString(), "", $"检查更新失败：{ex.Message}");
        }
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string DownloadPath { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}

public sealed record UpdateCheckResult(bool HasUpdate, string LatestVersion, string DownloadPath, string Message);
