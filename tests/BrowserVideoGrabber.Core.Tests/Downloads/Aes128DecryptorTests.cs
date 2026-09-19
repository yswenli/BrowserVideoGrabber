/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： Aes128DecryptorTests
*版本号： V1.0.0.0
*唯一标识：5f2e9e01-a811-441a-91c5-a886ce68a2d8
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:54:00
*描述：AES-128 解密器的单元测试，覆盖加解密往返、IV 推导与参数校验。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:54:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="Aes128Decryptor"/> 的行为验证。
/// </summary>
/// <remarks>
/// AES-128 加密的 HLS 不是「未来可能需要」的能力，而是真实站点已经在用的形态
/// （实测已见到 <c>crypt.key</c> 返回 16 字节密钥的清单）。解密一旦出错，
/// 产物会是一堆无法解码的噪声而不是明确的错误，因此加解密往返必须被测试固化。
/// </remarks>
public sealed class Aes128DecryptorTests
{
    /// <summary>
    /// 加密后再解密必须逐字节还原原文。
    /// </summary>
    [Fact]
    public void Decrypt_ShouldRoundTrip()
    {
        var key = CreateKey(0x11);
        var iv = Aes128Decryptor.BuildIv(0);
        var plain = Encoding.ASCII.GetBytes("MPEG-TS payload of exactly one segment.");

        var cipher = Encrypt(plain, key, iv);
        var restored = Aes128Decryptor.Decrypt(cipher, key, iv);

        Assert.Equal(plain, restored);
    }

    /// <summary>
    /// 解密结果必须与密文不同，避免「实现成直接返回输入」也能通过往返测试。
    /// </summary>
    [Fact]
    public void Decrypt_ShouldNotReturnCipherText()
    {
        var key = CreateKey(0x22);
        var iv = Aes128Decryptor.BuildIv(0);
        var plain = Encoding.ASCII.GetBytes("some plain text longer than one block");

        var cipher = Encrypt(plain, key, iv);

        Assert.NotEqual(cipher, Aes128Decryptor.Decrypt(cipher, key, iv));
    }

    /// <summary>
    /// 未声明 IV 时按 HLS 规范用媒体序号推导：128 位大端整数，序号位于最低字节。
    /// </summary>
    [Fact]
    public void BuildIv_ShouldEncodeSequenceAsBigEndian()
    {
        var iv = Aes128Decryptor.BuildIv(0x0102);

        Assert.Equal(16, iv.Length);
        Assert.Equal(0x01, iv[14]);
        Assert.Equal(0x02, iv[15]);
        Assert.All(iv[..14], b => Assert.Equal(0, b));
    }

    /// <summary>
    /// 清单显式声明的十六进制 IV 应被优先采用。
    /// </summary>
    [Fact]
    public void ParseIv_ShouldUseDeclaredHexIv()
    {
        var iv = Aes128Decryptor.ParseIv("0x000102030405060708090A0B0C0D0E0F", mediaSequenceNumber: 99);

        Assert.Equal(0x00, iv[0]);
        Assert.Equal(0x0F, iv[15]);
        Assert.Equal(0x0E, iv[14]);
    }

    /// <summary>
    /// 未声明 IV 时回退为媒体序号推导，而不是抛异常或使用全零向量。
    /// </summary>
    /// <param name="declared">清单中声明的 IV 文本。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseIv_ShouldFallBackToSequence(string? declared)
    {
        var iv = Aes128Decryptor.ParseIv(declared, mediaSequenceNumber: 5);

        Assert.Equal(Aes128Decryptor.BuildIv(5), iv);
    }

    /// <summary>
    /// 格式非法的 IV 不得抛出异常（清单来自不可控的第三方），应退化为序号推导。
    /// </summary>
    [Fact]
    public void ParseIv_ShouldFallBack_ForMalformedIv()
    {
        var iv = Aes128Decryptor.ParseIv("0xZZZ", mediaSequenceNumber: 3);

        Assert.Equal(Aes128Decryptor.BuildIv(3), iv);
    }

    /// <summary>
    /// 密钥长度不是 16 字节时必须明确拒绝，而不是让底层抛出难以理解的加密异常。
    /// </summary>
    [Fact]
    public void Decrypt_ShouldReject_ForInvalidKeyLength()
    {
        var iv = Aes128Decryptor.BuildIv(0);

        Assert.Throws<ArgumentException>(() => Aes128Decryptor.Decrypt([1, 2, 3, 4], new byte[15], iv));
    }

    /// <summary>
    /// IV 长度不是 16 字节时同样必须明确拒绝。
    /// </summary>
    [Fact]
    public void Decrypt_ShouldReject_ForInvalidIvLength()
    {
        Assert.Throws<ArgumentException>(() => Aes128Decryptor.Decrypt([1, 2, 3, 4], new byte[16], new byte[8]));
    }

    /// <summary>
    /// 流式重载的产物必须与字节数组重载一致。
    /// </summary>
    [Fact]
    public void DecryptStream_ShouldMatchByteArrayOverload()
    {
        var key = CreateKey(0x33);
        var iv = Aes128Decryptor.BuildIv(7);
        var plain = Encoding.ASCII.GetBytes("stream based decryption must agree");

        var cipher = Encrypt(plain, key, iv);

        using var input = new MemoryStream(cipher);
        using var output = new MemoryStream();
        Aes128Decryptor.Decrypt(input, output, key, iv);

        Assert.Equal(Aes128Decryptor.Decrypt(cipher, key, iv), output.ToArray());
    }

    /// <summary>
    /// 构造一个固定字节值的 16 字节密钥。
    /// </summary>
    /// <param name="value">填充值。</param>
    /// <returns>密钥。</returns>
    private static byte[] CreateKey(byte value)
    {
        var key = new byte[16];
        Array.Fill(key, value);
        return key;
    }

    /// <summary>
    /// 按 HLS 的 AES-128-CBC + PKCS7 约定加密，用于构造测试输入。
    /// </summary>
    /// <param name="plain">明文。</param>
    /// <param name="key">密钥。</param>
    /// <param name="iv">初始向量。</param>
    /// <returns>密文。</returns>
    private static byte[] Encrypt(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }
}
