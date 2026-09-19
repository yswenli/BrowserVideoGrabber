/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Json
*文件名： JsonModelsContext
*版本号： V1.0.0.0
*唯一标识：f7d2a9b4-0c3e-4f86-b1a2-9d5e6c7f8a9b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 16:00:00
*描述：System.Text.Json Source Generator 上下文，把所有 JSON 持久化 DTO 集中注册，
*      为 Native AOT 提供编译期生成的序列化器，避免运行时反射。
*
*=================================================
*修改标记
*修改时间：2026/9/13 16:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BrowserVideoGrabber.Core.Configuration;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Json;

/// <summary>
/// 统一 JSON Source Generator 上下文。
/// </summary>
/// <remarks>
/// <para>
/// Native AOT 下 <c>JsonSerializer.Deserialize{T}</c> 会抛异常 —— 反射版的序列化器被裁剪器剔掉了。
/// Source Generator 在编译期为每个标注的类型生成专用 <see cref="JsonTypeInfo{T}"/>，
/// 把元数据硬编进程序集，运行时直接查表、零反射。
/// </para>
/// <para>
/// 选在 Core 层而不是 Infrastructure 层，是因为 Core 是底层依赖，
/// 共享上下文不会引入循环引用；同时 AppHost（在 App 层）也能复用它来序列化恢复标签页的
/// <c>List&lt;string&gt;</c>。
/// </para>
/// <para>
/// <b>使用方式</b>：调用方不要直接 new <see cref="JsonSerializerOptions"/>，
/// 而是用 <c>JsonSerializer.Serialize(stream, value, JsonModelsContext.CreateOptions())</c>。
/// <see cref="CreateOptions"/> 返回的实例已经包含 Source Generator 产出的所有 TypeInfo，
/// 同时叠加了 <c>WriteIndented</c>、<c>UnsafeRelaxedJsonEscaping</c>、
/// <c>WhenWritingNull</c> 等 Repository 原有配置。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(FavoriteEntry))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(DownloadTask))]
[JsonSerializable(typeof(RequestContext))]
[JsonSerializable(typeof(VideoFormat))]
[JsonSerializable(typeof(DownloadStatus))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<FavoriteEntry>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(List<DownloadTask>))]
public partial class JsonModelsContext : JsonSerializerContext
{
    /// <summary>
    /// 创建已包含 Source Generator 全部 TypeInfo 与 Repository 常用配置的 Options 实例。
    /// </summary>
    /// <returns>可直接传给 <see cref="JsonSerializer.Serialize{TValue}(System.IO.Stream,TValue,JsonSerializerOptions?)"/> 的 Options。</returns>
    /// <remarks>
    /// 用 <c>AddJsonTypeInfo</c> 把 Source Generator 硬编码的 TypeInfo 注入进去，
    /// <b>不涉及任何运行时反射</b> —— 这正是 Native AOT 下正确叠加配置的姿势。
    /// </remarks>
    public static JsonSerializerOptions CreateOptions()
    {
        // Source Generator 产出的 Default 继承自 JsonSerializerContext，
        // 本身就是 IJsonTypeInfoResolver —— 把它赋给 TypeInfoResolver，
        // 所有序列化/反序列化就自动走编译期硬编码的 TypeInfo，零反射。
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonModelsContext.Default,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return options;
    }
}