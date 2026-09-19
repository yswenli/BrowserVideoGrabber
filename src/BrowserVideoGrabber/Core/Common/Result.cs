/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Common
*文件名： Result
*版本号： V1.0.0.0
*唯一标识：6ab6f768-4ca2-4ca1-9456-b4b80bee0c35
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:19:00
*描述：通用结果包装类型，用于在不抛异常的前提下表达成功/失败与错误原因。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:19:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Common;

/// <summary>
/// 通用操作结果（无返回值版本）。
/// </summary>
/// <remarks>
/// 用于「解析失败」「探测失败」这类可预期错误：这类情况属于正常业务分支，
/// 若一律抛异常会让调用方充满 try/catch，也会让单元测试难以表达断言意图。
/// </remarks>
public class Result
{
    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>失败原因（中文描述）。成功时为空。</summary>
    public string? Error { get; }

    /// <summary>
    /// 受保护构造函数，请通过 <see cref="Ok()"/> / <see cref="Fail"/> 创建实例。
    /// </summary>
    /// <param name="isSuccess">是否成功。</param>
    /// <param name="error">失败原因。</param>
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>创建成功结果。</summary>
    /// <returns>成功结果实例。</returns>
    public static Result Ok() => new(true, null);

    /// <summary>创建失败结果。</summary>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <returns>失败结果实例。</returns>
    public static Result Fail(string error) => new(false, error);

    /// <summary>创建带返回值的成功结果。</summary>
    /// <typeparam name="TValue">返回值类型。</typeparam>
    /// <param name="value">返回值。</param>
    /// <returns>成功结果实例。</returns>
    public static Result<TValue> Ok<TValue>(TValue value) => new(value, true, null);

    /// <summary>创建带返回值类型的失败结果。</summary>
    /// <typeparam name="TValue">返回值类型。</typeparam>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <returns>失败结果实例。</returns>
    public static Result<TValue> Fail<TValue>(string error) => new(default, false, error);

    /// <summary>
    /// 将当前结果转换为带返回值的版本。
    /// </summary>
    /// <typeparam name="TValue">目标返回值类型。</typeparam>
    /// <param name="value">成功时携带的返回值。</param>
    /// <returns>转换后的结果实例。</returns>
    public Result<TValue> Cast<TValue>(TValue value)
        => new(value, IsSuccess, Error);
}

/// <summary>
/// 通用操作结果（带返回值版本）。
/// </summary>
/// <typeparam name="TValue">成功时携带的返回值类型。</typeparam>
public sealed class Result<TValue> : Result
{
    /// <summary>成功时的返回值。失败时为默认值。</summary>
    public TValue? Value { get; }

    /// <summary>
    /// 内部构造函数，请通过 <see cref="Result.Ok{TValue}"/> / <see cref="Result.Fail{TValue}"/> 创建实例。
    /// </summary>
    /// <param name="value">返回值。</param>
    /// <param name="isSuccess">是否成功。</param>
    /// <param name="error">失败原因。</param>
    internal Result(TValue? value, bool isSuccess, string? error)
        : base(isSuccess, error)
    {
        Value = value;
    }
}
