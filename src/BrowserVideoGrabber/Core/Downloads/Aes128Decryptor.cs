/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： Aes128Decryptor
*版本号： V1.0.0.0
*唯一标识：84e5816f-16eb-46a6-817b-c2908785c7fc
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:44:00
*描述：AES-128 解密器，按 HLS 规范实现 CBC + PKCS7 解密，支持由媒体序号或清单显式 IV 推导初始向量。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:44:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using System.Security.Cryptography;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// AES-128 解密器：HLS 标准采用 AES-128-CBC + PKCS7，密钥长度固定 16 字节。
/// </summary>
/// <remarks>
/// 解密是「C# 取分片」链路里无法交给 ffmpeg 直连的一环：ffmpeg 直连时密钥由它自己管理，
/// 我们拿不到逐片明文也就做不了分片级校验。把解密收口到此处，
/// 取片编排才能在写盘前对每一片先校验再解密，被污染的分片直接丢弃而非污染成品。
/// </remarks>
public static class Aes128Decryptor
{
    /// <summary>密钥长度（字节）。</summary>
    public const int KeySizeBytes = 16;

    /// <summary>初始向量长度（字节）。</summary>
    public const int IvSizeBytes = 16;

    /// <summary>
    /// 按 HLS 规范由媒体序号推导 IV：128 位大端整数，序号占据最低字节。
    /// </summary>
    /// <param name="mediaSequenceNumber">分片在清单中的媒体序号（#EXT-X-MEDIA-SEQUENCE）。</param>
    /// <returns>16 字节初始向量。</returns>
    public static byte[] BuildIv(long mediaSequenceNumber)
    {
        // 大端写入：序号占据最低字节。注意 long 右移的移位量会被掩码到 6 位（count & 0x3F），
        // 超过 63 的移位量会回绕而非清零，因此这里按目标下标反推移位量，并对越界位移直接置 0。
        var iv = new byte[IvSizeBytes];
        for (var i = 0; i < IvSizeBytes; i++)
        {
            var shift = 8 * (IvSizeBytes - 1 - i);
            iv[i] = shift < 64 ? (byte)(mediaSequenceNumber >> shift) : (byte)0;
        }

        return iv;
    }

    /// <summary>
    /// 解析清单中声明的 IV 文本；非法或缺失时回退为媒体序号推导。
    /// </summary>
    /// <param name="declared">清单 <c>#EXT-X-KEY</c> 的 IV 字段，形如 <c>0x...</c> 或纯十六进制。</param>
    /// <param name="mediaSequenceNumber">媒体序号，作为回退值。</param>
    /// <returns>16 字节初始向量。</returns>
    public static byte[] ParseIv(string? declared, long mediaSequenceNumber)
    {
        if (!string.IsNullOrWhiteSpace(declared))
        {
            var hex = declared!.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
            }

            if (hex.Length == IvSizeBytes * 2 && TryParseHex(hex, out var iv))
            {
                return iv;
            }
        }

        // 清单来自不可控的第三方，格式异常时宁可退化为序号推导，也不要抛异常打断整批下载
        return BuildIv(mediaSequenceNumber);
    }

    /// <summary>
    /// 解密整段密文为明文。
    /// </summary>
    /// <param name="cipher">密文。</param>
    /// <param name="key">16 字节密钥。</param>
    /// <param name="iv">16 字节初始向量。</param>
    /// <returns>明文。</returns>
    public static byte[] Decrypt(byte[] cipher, byte[] key, byte[] iv)
    {
        Validate(key, iv);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>
    /// 流式解密，适用于大分片以避免整段密文/明文驻留内存。
    /// </summary>
    /// <param name="input">密文输入流。</param>
    /// <param name="output">明文输出流。</param>
    /// <param name="key">16 字节密钥。</param>
    /// <param name="iv">16 字节初始向量。</param>
    public static void Decrypt(Stream input, Stream output, byte[] key, byte[] iv)
    {
        Validate(key, iv);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        using var cryptoStream = new CryptoStream(output, decryptor, CryptoStreamMode.Write);
        input.CopyTo(cryptoStream);
        cryptoStream.FlushFinalBlock();
    }

    /// <summary>
    /// 校验密钥与 IV 长度，非法时抛出明确的参数异常而非底层加密异常。
    /// </summary>
    private static void Validate(byte[] key, byte[] iv)
    {
        if (key is null || key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"AES-128 密钥必须为 {KeySizeBytes} 字节", nameof(key));
        }

        if (iv is null || iv.Length != IvSizeBytes)
        {
            throw new ArgumentException($"IV 必须为 {IvSizeBytes} 字节", nameof(iv));
        }
    }

    /// <summary>
    /// 按字节解析十六进制字符串；含非法字符时返回 false。
    /// </summary>
    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, null, out bytes[i]))
            {
                return false;
            }
        }

        return true;
    }
}
