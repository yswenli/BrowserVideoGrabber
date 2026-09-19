/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Common
*文件名： RetryPolicy
*版本号： V1.0.0.0
*唯一标识：5a28e0f0-9aff-46f1-ac8f-1392631f7ca1
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:20:00
*描述：指数退避重试策略，控制下载失败后的重试次数与等待间隔。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Common;

/// <summary>
/// 指数退避重试策略。
/// </summary>
/// <remarks>
/// <para>
/// 等待时长按 <c>基础间隔 × 2^(已重试次数 - 1)</c> 增长，并受 <see cref="MaxDelay"/> 上限约束。
/// 默认序列为 1s、2s、4s（上限 30s）。
/// </para>
/// <para>
/// 之所以采用指数退避而非固定间隔：流媒体站点多为瞬时限流或 CDN 抖动，
/// 立即重试往往继续失败，逐次拉长间隔能显著提高第二次、第三次的命中率。
/// </para>
/// </remarks>
public sealed class RetryPolicy
{
    /// <summary>最大尝试次数（含首次尝试）。默认 3 次。</summary>
    public int MaxAttempts { get; }

    /// <summary>首次重试的基础等待间隔。默认 1 秒。</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>单次等待间隔上限。默认 30 秒。</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// 初始化重试策略。
    /// </summary>
    /// <param name="maxAttempts">最大尝试次数（含首次），小于 1 时按 1 处理。</param>
    /// <param name="baseDelay">基础等待间隔，为空时取 1 秒。</param>
    /// <param name="maxDelay">等待间隔上限，为空时取 30 秒。</param>
    public RetryPolicy(int maxAttempts = 3, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        MaxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        BaseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        MaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 判断在指定重试次数后是否还应继续重试。
    /// </summary>
    /// <param name="retryCount">已重试次数（首次失败后为 1）。</param>
    /// <returns>还应重试返回 true；已达到上限返回 false。</returns>
    public bool ShouldRetry(int retryCount) => retryCount < MaxAttempts;

    /// <summary>
    /// 计算第 N 次重试前应等待的时长。
    /// </summary>
    /// <param name="retryCount">已重试次数（从 1 开始）。</param>
    /// <returns>本次重试的等待时长，介于 <see cref="BaseDelay"/> 与 <see cref="MaxDelay"/> 之间。</returns>
    public TimeSpan GetDelay(int retryCount)
    {
        // retryCount 小于 1 时按第 1 次处理，避免出现 0.5 倍这种非预期间隔
        var effectiveCount = retryCount < 1 ? 1 : retryCount;

        // 指数增长：base * 2^(n-1)；指数用 double 计算后再裁剪，避免 long 溢出
        var milliseconds = BaseDelay.TotalMilliseconds * Math.Pow(2, effectiveCount - 1);
        var capped = Math.Min(milliseconds, MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(capped);
    }

    /// <summary>
    /// 按策略等待一段时间。
    /// </summary>
    /// <param name="retryCount">已重试次数（从 1 开始）。</param>
    /// <param name="cancellationToken">取消令牌，取消时立即抛出 <see cref="OperationCanceledException"/>。</param>
    /// <returns>表示异步等待的任务。</returns>
    public Task WaitAsync(int retryCount, CancellationToken cancellationToken)
        => Task.Delay(GetDelay(retryCount), cancellationToken);
}
