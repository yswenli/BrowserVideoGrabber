/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： FileNameBuilder
*版本号： V1.0.0.0
*唯一标识：e9f98f64-816c-4c50-8ebb-1755d95961c7
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:00:00
*描述：下载文件名派生工具：把页面标题转换为合法、定长的文件名主干。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载文件名主干的派生工具。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是纯静态函数且放在 Core</b>：文件名派生只依赖「标题字符串」这一个输入，
/// 既不需要磁盘也不需要 UI，放在 Core 才能被单元测试覆盖。
/// 它原先作为私有方法写在 <c>AppHost</c> 里，而 App 层没有测试工程 ——
/// 这正是「页面标题被静默丢弃却无人发现」的原因：链路断了也测不到。
/// </para>
/// <para>
/// <b>截断发生在清洗之后</b>：若先截断再替换非法字符，前 15 个字符里可能有一半是
/// <c>?</c> <c>*</c> <c>:</c>，替换后实际只剩几个有效字符。先清洗再截断才能保证「最多 15 个可用字符」。
/// </para>
/// </remarks>
public static class FileNameBuilder
{
    /// <summary>页面标题作为文件名时的最大字符数。</summary>
    public const int DefaultMaxLength = 15;

    /// <summary>
    /// 回退标题（由 URL 派生）的最大字符数。
    /// </summary>
    /// <remarks>
    /// 比页面标题宽松：URL 末段往往就是资源的真实名字（<c>episode-12.mp4</c>），
    /// 截断到 15 会把它切得没有辨识度。
    /// </remarks>
    public const int FallbackMaxLength = 80;

    /// <summary>所有来源都拿不到有效字符时的兜底名。</summary>
    public const string FallbackName = "video";

    /// <summary>
    /// 派生文件名主干（不含目录与扩展名）。
    /// </summary>
    /// <param name="pageTitle">页面标题，优先使用。</param>
    /// <param name="fallbackTitle">回退标题，通常由 URL 派生。</param>
    /// <param name="maxLength">页面标题的最大字符数，默认 <see cref="DefaultMaxLength"/>。</param>
    /// <returns>
    /// 合法的文件名主干，永不为空。
    /// </returns>
    /// <remarks>
    /// 优先级为「页面标题 → 回退标题 → <see cref="FallbackName"/>」：
    /// 页面标题最贴近用户在列表上看到的名字，但它可能尚未加载出来（导航未完成就入队），
    /// 因此必须保留 URL 派生名作为回退，最后再兜底一个固定名，避免出现空文件名。
    /// </remarks>
    public static string Build(string? pageTitle, string? fallbackTitle, int maxLength = DefaultMaxLength)
    {
        if (maxLength <= 0)
        {
            maxLength = DefaultMaxLength;
        }

        var fromPage = Truncate(Sanitize(pageTitle), maxLength);
        if (!string.IsNullOrWhiteSpace(fromPage))
        {
            return fromPage;
        }

        var fromUrl = Truncate(Sanitize(fallbackTitle), FallbackMaxLength);
        return string.IsNullOrWhiteSpace(fromUrl) ? FallbackName : fromUrl;
    }

    /// <summary>
    /// 替换文件系统不允许的字符，并去掉首尾空白与点号。
    /// </summary>
    /// <param name="name">原始名字。</param>
    /// <returns>清洗后的名字；输入为空时返回空串。</returns>
    /// <remarks>
    /// 去掉首尾点号是 Windows 的实际约束：以点号结尾的文件名会被系统自动去掉尾部点，
    /// 于是「写入 A. 实际得到 A」—— 后续按 A. 查找就会找不到。
    /// </remarks>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var sanitized = builder.ToString().Trim().Trim('.');

        // 整串都是被替换出来的下划线（标题是 "???"、"///" 这类占位）时视为无效：
        // 把它当作文件名会产出一个毫无辨识度的 "___"，不如回退到 URL 派生名
        return sanitized.All(character => character == '_') ? string.Empty : sanitized;
    }

    /// <summary>
    /// 将字符串截断到指定字符数。
    /// </summary>
    /// <param name="value">原始字符串。</param>
    /// <param name="maxLength">最大字符数。</param>
    /// <returns>截断后的字符串；原长不足则原样返回。</returns>
    /// <remarks>
    /// 截断位置若落在代理项对的高半区，则回退一个字符 —— 否则会产生半个 Unicode 字符，
    /// 序列化成 JSON 或写进日志时变成乱码问号。
    /// </remarks>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;

        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        // 截断后可能以点号结尾（如 "abcdefghijklmno."），同样需要清掉尾部点号
        return length <= 0 ? string.Empty : value[..length].TrimEnd('.', ' ');
    }
}
