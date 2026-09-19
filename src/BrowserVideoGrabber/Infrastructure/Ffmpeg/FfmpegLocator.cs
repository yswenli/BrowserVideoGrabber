/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Ffmpeg
*文件名： FfmpegLocator
*版本号： V1.0.0.0
*唯一标识：875c4533-f317-47cf-af70-309ca8af57f0
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:16:00
*描述：ffmpeg 可执行文件定位器，按优先级探测本机可用的 ffmpeg 路径。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:16:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Infrastructure.Ffmpeg;

/// <summary>
/// ffmpeg 可执行文件定位器。
/// </summary>
/// <remarks>
/// <para>
/// 本工具<b>不随包分发 ffmpeg 二进制</b>（体积大、许可与升级维护成本高），
/// 改为运行时按以下优先级探测：
/// <list type="number">
///   <item><description>用户在设置中显式指定的路径（优先级最高，便于用户覆盖探测结果）</description></item>
///   <item><description>应用目录下 <c>tools\ffmpeg\ffmpeg.exe</c>（便于绿色版一起打包）</description></item>
///   <item><description>应用目录下 <c>ffmpeg.exe</c></description></item>
///   <item><description>系统 PATH 环境变量</description></item>
///   <item><description>常见包管理器安装位置（scoop / chocolatey）与常见手动安装位置</description></item>
/// </list>
/// </para>
/// <para>
/// 探测结果会被缓存：定位操作会在每个任务开始时执行一次，
/// 若每次都遍历 PATH 与环境变量会造成明显的无谓开销。
/// 用户修改设置后需调用 <see cref="ClearCache"/> 使缓存失效。
/// </para>
/// </remarks>
public sealed class FfmpegLocator
{
    /// <summary>可执行文件名。Windows 平台固定为 ffmpeg.exe。</summary>
    private const string ExecutableName = "ffmpeg.exe";

    private readonly IFileSystem _fileSystem;
    private readonly string? _configuredPath;
    private readonly string? _appDirectory;
    private readonly bool _enableEnvironmentSearch;

    private string? _cachedPath;

    /// <summary>
    /// 初始化定位器。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象，用于判断候选路径是否真实存在。</param>
    /// <param name="configuredPath">用户显式配置的 ffmpeg 路径，可为空。</param>
    /// <param name="appDirectory">应用所在目录（通常是可执行文件所在目录），可为空。</param>
    /// <param name="enableEnvironmentSearch">
    /// 是否允许搜索 PATH 与常见安装位置。默认 true；
    /// 单元测试中会关闭该选项，以保证「未找到 ffmpeg」分支可被确定性验证。
    /// </param>
    public FfmpegLocator(
        IFileSystem fileSystem,
        string? configuredPath = null,
        string? appDirectory = null,
        bool enableEnvironmentSearch = true)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configuredPath = configuredPath;
        _appDirectory = appDirectory;
        _enableEnvironmentSearch = enableEnvironmentSearch;
    }

    /// <summary>
    /// 定位 ffmpeg 可执行文件。
    /// </summary>
    /// <returns>ffmpeg 的完整路径；全部候选均不存在时返回 null。</returns>
    public string? Locate()
    {
        if (_cachedPath is not null)
        {
            return _cachedPath;
        }

        foreach (var candidate in GetCandidatePaths())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (_fileSystem.FileExists(candidate))
            {
                _cachedPath = candidate;
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断本机是否可用 ffmpeg。
    /// </summary>
    /// <returns>可用返回 true。</returns>
    public bool IsAvailable() => Locate() is not null;

    /// <summary>
    /// 清除探测缓存，强制下次重新探测。用户修改设置后必须调用。
    /// </summary>
    public void ClearCache() => _cachedPath = null;

    /// <summary>
    /// 按优先级枚举全部候选路径。
    /// </summary>
    /// <returns>候选路径序列（可能包含空项，由调用方跳过）。</returns>
    public IEnumerable<string> GetCandidatePaths()
    {
        // 1) 用户显式配置
        yield return _configuredPath ?? string.Empty;

        // 2) 应用目录内的随包位置
        if (!string.IsNullOrWhiteSpace(_appDirectory))
        {
            yield return Path.Combine(_appDirectory, "tools", "ffmpeg", ExecutableName);
            yield return Path.Combine(_appDirectory, ExecutableName);
        }

        if (!_enableEnvironmentSearch)
        {
            yield break;
        }

        // 3) 系统 PATH
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = directory.Trim().Trim('"');
                if (trimmed.Length > 0)
                {
                    yield return Path.Combine(trimmed, ExecutableName);
                }
            }
        }

        // 4) 常见包管理器与手动安装位置
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            // scoop 默认的 shim 目录
            yield return Path.Combine(userProfile, "scoop", "shims", ExecutableName);
        }

        yield return @"C:\ProgramData\chocolatey\bin\" + ExecutableName;
        yield return @"C:\ffmpeg\bin\" + ExecutableName;
    }
}
