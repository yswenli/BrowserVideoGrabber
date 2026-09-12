# BrowserVideoGrabber 设计文档

- **文档版本**：V1.0.0.0
- **创建时间**：2026/9/12 22:05:00
- **创建人**：yswenli
- **状态**：已评审通过（2026/9/12）

---

## 1. 目标与范围

### 1.1 目标

实现一个 Windows 桌面端「浏览器 + 视频嗅探 + 视频下载」一体化工具：

1. 左侧为主浏览器界面，支持地址栏输入 URL、前进 / 后退 / 刷新，内嵌 Chromium 内核渲染页面；
2. 右上侧为「页面嗅探」面板，实时列出当前页面加载过程中出现的视频资源；
3. 右下侧为「下载列表」，以三个页签组织：**待下载 / 正在下载 / 已下载**；
4. 支持 `m3u8` / `ts` / `m4s` / `mp4`（含 `mpd` DASH 索引）四类资源的下载；
5. 下载能力联合外部 `ffmpeg.exe` 实现（分片合并、AES-128 解密、DASH 拼装），`mp4` 走原生多线程下载。

### 1.2 明确不做（范围边界）

| 不做的事 | 原因 |
|---|---|
| DRM（Widevine / PlayReady / SAMPLE-AES）解密 | 商业 DRM 需授权许可，技术上不可行也不合规 |
| 直播流长期录制 | 首版聚焦点播（VOD），直播留待后续迭代 |
| 浏览器插件 / 油猴脚本形态 | 本产品定位为独立桌面工具 |
| 视频转码压缩 | 首版只做「原样下载」，`-c copy` 不重编码 |
| 自动绕过站点风控 / 验证码 | 仅复用用户已登录的浏览器会话 |

---

## 2. 技术选型

| 维度 | 选型 | 理由 |
|---|---|---|
| 运行时 | **.NET 10**（`net10.0-windows`） | 用户指定；本机已装 SDK 10.0.401 |
| UI 框架 | **Windows Forms** | 用户指定 |
| 浏览器内核 | **WebView2**（Microsoft.Web.WebView2） | Edge Chromium 内核；原生暴露网络响应事件与 CookieManager；Win10/11 自带 Evergreen 运行时 |
| 嗅探方式 | 网络响应监听 + URL 正则 + JS 注入 Hook | 三路互补，覆盖直链与动态 / blob 地址 |
| 下载引擎 | ffmpeg 优先 + 原生 HTTP 兜底 | 兼顾成功率（分片 / 加密）与速度（mp4 多线程） |
| 测试框架 | xUnit | 生态成熟，与 `dotnet test` 集成好 |
| 依赖注入 | 手写组合根（`AppHost`） | 规模不大，避免引入 DI 容器的额外复杂度（YAGNI） |

### 2.1 ffmpeg 分发策略（决策记录）

**不随包分发 ffmpeg 二进制**（体积大、许可与升级维护成本高），改为**运行时按序探测**：

1. 用户设置项中显式指定的路径；
2. 应用目录下 `tools\ffmpeg\ffmpeg.exe`；
3. 应用目录下 `ffmpeg.exe`；
4. 系统 `PATH` 环境变量；
5. 常见安装路径（如 `scoop` / `choco` 默认目录）。

全部未命中时，主界面顶部显示警告横幅并引导用户到设置页指定路径；`m3u8` / `ts` / `m4s` 类任务会直接失败并给出明确提示。

---

## 3. 架构设计

### 3.1 分层与依赖方向

```
App (WinForms)  ──依赖──▶  Core (抽象)
                              ▲
Infrastructure ──实现──────────┘
tests/Core.Tests ──测试──▶ Core
```

- **Core**：领域模型 + 抽象接口 + 纯逻辑算法（URL 匹配、m3u8 解析、ffmpeg 参数构建、进度解析、下载队列调度）。**零 UI 依赖、零 Windows 依赖**，是整个 TDD 的主战场。
- **Infrastructure**：具体实现（WebView2 嗅探、JS 注入、HTTP 下载、ffmpeg 下载、进程运行、JSON 持久化、Cookie 导出）。可整体替换而不影响 Core 与 App。
- **App**：只做界面与事件绑定，业务逻辑一律下沉到 Core。

### 3.2 目录结构

```
BrowserVideoGrabber/
├─ BrowserVideoGrabber.sln
├─ Directory.Build.props                     # 统一 TFM / Nullable / LangVersion / 程序集信息
├─ docs/plans/                               # 设计文档与实现计划
├─ src/
│  ├─ BrowserVideoGrabber.App/               # net10.0-windows · WinExe
│  │  ├─ Program.cs
│  │  ├─ AppHost.cs                          # 组合根：装配 Core + Infrastructure
│  │  ├─ MainForm.cs
│  │  ├─ Panes/BrowserPane.cs                # 地址栏 + WebView2 宿主
│  │  ├─ Panes/SniffPane.cs                  # 嗅探结果列表
│  │  ├─ Panes/DownloadPane.cs               # TabControl 三页签
│  │  ├─ Binding/SniffListBinder.cs
│  │  ├─ Binding/DownloadListBinder.cs       # 进度节流刷新
│  │  ├─ Dialogs/SettingsForm.cs
│  │  └─ AppSettings.cs
│  ├─ BrowserVideoGrabber.Core/              # net10.0 · 零 UI 依赖
│  │  ├─ Models/
│  │  │  ├─ VideoFormat.cs
│  │  │  ├─ SniffedVideo.cs
│  │  │  ├─ RequestContext.cs
│  │  │  ├─ DownloadStatus.cs
│  │  │  ├─ DownloadTask.cs
│  │  │  ├─ DownloadProgress.cs
│  │  │  └─ DownloadResult.cs
│  │  ├─ Abstractions/
│  │  │  ├─ IVideoSniffer.cs
│  │  │  ├─ IDownloadHandler.cs
│  │  │  ├─ IProcessRunner.cs
│  │  │  ├─ IFileSystem.cs
│  │  │  └─ ITaskRepository.cs
│  │  ├─ Sniffing/
│  │  │  └─ VideoUrlMatcher.cs
│  │  ├─ Downloads/
│  │  │  ├─ DownloadQueue.cs
│  │  │  ├─ DownloadHandlerFactory.cs
│  │  │  ├─ M3u8Playlist.cs
│  │  │  └─ M3u8Parser.cs
│  │  ├─ Ffmpeg/
│  │  │  ├─ FfmpegOptions.cs
│  │  │  ├─ FfmpegArgumentBuilder.cs
│  │  │  ├─ FfmpegProgressParser.cs
│  │  │  └─ FfmpegLocator.cs
│  │  └─ Common/
│  │     ├─ Result.cs
│  │     └─ RetryPolicy.cs
│  └─ BrowserVideoGrabber.Infrastructure/    # net10.0-windows
│     ├─ Sniffing/
│     │  ├─ NetworkSniffRules.cs
│     │  ├─ JsHookScript.cs                  # 注入脚本常量
│     │  ├─ JsHookInjector.cs
│     │  └─ WebView2Sniffer.cs
│     ├─ Downloads/
│     │  ├─ HttpDownloadHandler.cs
│     │  └─ FfmpegDownloadHandler.cs
│     ├─ Process/ProcessRunner.cs
│     ├─ Storage/JsonTaskRepository.cs
│     └─ Security/CookieExporter.cs
└─ tests/
   └─ BrowserVideoGrabber.Core.Tests/        # net10.0 · xUnit
      ├─ Fakes/FakeProcessRunner.cs
      ├─ Fakes/FakeFileSystem.cs
      ├─ Sniffing/VideoUrlMatcherTests.cs
      ├─ Downloads/M3u8ParserTests.cs
      ├─ Downloads/DownloadQueueTests.cs
      └─ Ffmpeg/FfmpegArgumentBuilderTests.cs
      └─ Ffmpeg/FfmpegProgressParserTests.cs
```

---

## 4. 关键设计

### 4.1 嗅探：三路信号互补

| 链路 | 实现位置 | 信号 | 局限 |
|---|---|---|---|
| 响应监听 | `WebView2Sniffer` | `WebResourceResponseReceived` 响应头 Content-Type ∈ {`application/vnd.apple.mpegurl`, `application/x-mpegURL`, `video/mp4`, `video/mp2t`, `application/dash+xml`} | 拿不到 `blob:` 地址 |
| URL 正则 | `VideoUrlMatcher` | URL 后缀匹配 `\.(m3u8|ts|m4s|mp4|mpd)` | 存在误报，需按 URL 去重 |
| JS 注入 Hook | `JsHookInjector` + `WebView2Sniffer` | 劫持 `XMLHttpRequest.prototype.open`、`window.fetch`、`MediaSource.prototype.addSourceBuffer`，经 `chrome.webview.postMessage` 回传 | 少数站点 CSP 或代码混淆对抗 |

三路产出的原始 URL 统一交给 `VideoUrlMatcher.Normalize` 归一化（去 fragment、统一小写 scheme/host、剥离重复 query）并按 URL 去重，最终形成 `SniffedVideo` 列表。

**JS Hook 注入时机**：`WebView2.NavigationStarting` 时通过 `AddScriptToExecuteOnDocumentCreatedAsync` 注册，保证在页面脚本执行前生效。

### 4.2 格式与下载策略

| 格式 | 结构 | 处理器 | 说明 |
|---|---|---|---|
| `mp4` | 单文件，支持 HTTP Range | `HttpDownloadHandler` | 多线程分片 + 断点续传；失败回退 ffmpeg |
| `ts` | 连续分片序列 | `FfmpegDownloadHandler` | 由 ffmpeg 拉取并合并 |
| `m3u8` | 索引文件（指向 ts / fMP4，可 AES-128 加密） | `FfmpegDownloadHandler` | `-i <m3u8> -c copy`，ffmpeg 自动处理分片、解密、合并 |
| `m4s` | fMP4 分片（DASH），需 init segment | `FfmpegDownloadHandler` | 优先对 `mpd` 索引执行；无索引时按 init + 分片顺序拼装 |
| `mpd` | DASH 索引 | `FfmpegDownloadHandler` | 直接交给 ffmpeg |

### 4.3 反爬与鉴权

- 下载请求必须携带与内嵌浏览器一致的 **Referer / User-Agent / Origin / Cookie**，否则高概率 403；
- `CookieExporter` 在任务入队时通过 `CoreWebView2.CookieManager.GetCookiesAsync` 导出目标域 Cookie，写入 `RequestContext`；
- `FfmpegArgumentBuilder` 将其转换为 `-headers "Referer: ...\r\nUser-Agent: ...\r\nCookie: ...\r\n"`；`HttpDownloadHandler` 直接设置 `HttpRequestMessage.Headers`；
- **加密能力边界**：`#EXT-X-KEY:METHOD=AES-128` 可支持（ffmpeg 自动取 key 解密）；`METHOD=SAMPLE-AES` 或 DRM `#EXT-X-SESSION-KEY` 视为受保护内容，**直接判定失败并给出明确提示**，不静默重试。

### 4.4 下载队列与状态机

```
                  ┌──────────── 重试 (RetryPolicy) ────────────┐
                  ▼                                             │
   pending ──▶ running ──▶ completed                             │
                  │ ├────▶ failed ──────────────────────────────┘
                  │ └────▶ canceled（终态）
                  └──────▶ paused ──▶ pending
```

- `DownloadQueue` 维护 `pending / running / paused / completed / failed / canceled` 六个集合视图；
- 并发上限 `MaxConcurrency`（默认 3，可配置），用 `SemaphoreSlim` 控制；
- 每个 `DownloadTask` 绑定 `CancellationTokenSource`，取消时 `Process.Kill(entireProcessTree: true)` 并清理半成品；
- 队列在 UI 线程外运行，状态变更通过事件 + `SynchronizationContext` 回主线程。

### 4.5 进度采集

| 来源 | 采集方式 |
|---|---|
| ffmpeg | 解析 **stderr** 行中的 `Duration: HH:MM:SS.xx`（总时长）与 `time=HH:MM:SS.xx`（当前进度），百分比 = time / Duration；同时解析 `speed=` |
| 原生 HTTP | 已下载字节 / `Content-Length` |

统一产出 `DownloadProgress`（百分比 / 已处理时长 / 总时长 / 速度 / 已下载字节）。UI 侧由 `DownloadListBinder` 做 **200ms 节流** 刷新，避免高频重绘。

### 4.6 错误处理

| 场景 | 处理策略 |
|---|---|
| ffmpeg 未找到 | 启动探测 → 顶部横幅警告 + 设置页引导；任务失败信息注明「未找到 ffmpeg」 |
| HTTP 401 / 403 | 携带完整 `RequestContext` 重发一次；仍失败则标 `failed`，提示「可能需登录或缺少 Referer」 |
| 网络抖动 / 超时 | `RetryPolicy` 指数退避（1s/2s/4s…，上限 3 次）；已下分片保留，续传 |
| AES-128 加密 | 正常支持 |
| DRM 受保护 | 标 `failed`，提示「受保护内容，本工具不支持 DRM」 |
| 磁盘空间不足 | 任务启动前预检可用空间；失败时清理半成品文件 |
| 用户取消 | 终止进程树 + 删除 `.part` / 临时分片 |
| 输出目录不存在 | 自动创建；无权限则回退到「下载」目录并提示 |

### 4.7 线程模型

- **UI 线程**：仅负责界面渲染与事件绑定；
- **下载线程**：`Task` + `SemaphoreSlim`，全部在 ThreadPool；
- **进度回传**：`IProgress<T>`（`Progress<T>` 自动捕获 `SynchronizationContext`）→ 主线程 `BeginInvoke` 刷新；
- **禁止**在下载线程直接触碰任何 WinForms 控件。

---

## 5. 核心接口契约

```csharp
/// <summary>视频嗅探器抽象</summary>
public interface IVideoSniffer
{
    event EventHandler<SniffedVideo>? VideoDetected;
    void Start();
    void Stop();
}

/// <summary>下载处理器抽象（按格式策略分发）</summary>
public interface IDownloadHandler
{
    bool CanHandle(DownloadTask task);
    Task<DownloadResult> DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}

/// <summary>进程运行器抽象（使 ffmpeg 逻辑可脱离真实进程单测）</summary>
public interface IProcessRunner
{
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string> onStandardErrorLine,
        CancellationToken cancellationToken);
}

/// <summary>文件系统抽象（使下载逻辑可脱离真实磁盘单测）</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    Stream OpenWrite(string path, bool append);
    long GetAvailableFreeSpace(string path);
}

/// <summary>任务持久化抽象</summary>
public interface ITaskRepository
{
    Task<IReadOnlyList<DownloadTask>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<DownloadTask> tasks, CancellationToken cancellationToken = default);
}
```

---

## 6. 测试策略

**Core 层全部纯单元测试**（不触网、不触盘、不起真实进程）：

| 被测对象 | 测试要点 |
|---|---|
| `VideoUrlMatcher` | 各格式识别、query 干扰、大小写、去重、非视频 URL 拒绝 |
| `M3u8Parser` | 主 / 子 playlist、`#EXT-X-KEY` 加密判定、`#EXT-X-STREAM-INF` 清晰度提取、`#EXT-X-MAP` init 段 |
| `FfmpegArgumentBuilder` | `-headers` 转义、`-c copy`、输出路径、重连参数、参数顺序 |
| `FfmpegProgressParser` | `Duration` / `time=` 提取、百分比计算、跨行增量解析、脏行容错 |
| `DownloadQueue` | 并发上限、状态流转、重试计数、取消、失败隔离 |

**Infrastructure / App**：编写手工冒烟清单（真实站点嗅探 + 四种格式各下一遍 + 取消 / 续传 / 403 场景）。

---

## 7. 代码规范

### 7.1 文件头注释模板（**强制**）

每个 `.cs` 文件必须以如下块注释开头，字段逐文件填写（`唯一标识` 必须全局唯一，`命名空间` / `文件名` / `描述` 与该文件实际内容一致）：

```csharp
/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： DownloadTask
*版本号： V1.0.0.0
*唯一标识：5ceeef21-3dfa-4b80-85da-73068cbdda52
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:05:00
*描述：下载任务领域模型，承载单个视频资源的下载上下文与运行状态。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/
```

### 7.2 注释要求

1. **文件头注释**：如上，所有 `.cs` 文件无一例外；
2. **类型注释**：所有 `class` / `interface` / `record` / `enum` 必须有 `/// <summary>` 中文说明，关键类型补充 `<remarks>`；
3. **成员注释**：所有 `public` / `protected` 成员必须有 `/// <summary>`，参数与返回值用 `<param>` / `<returns>` 补全；
4. **行内注释**：非直观逻辑（正则含义、ffmpeg 参数作用、协议细节、边界条件）必须有中文行内注释说明「为什么」而非「是什么」；
5. **枚举成员**：每个枚举值都要有注释，说明其业务语义；
6. 注释与代码同步维护，禁止保留与新代码矛盾的过期注释。

---

## 8. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| WebView2 运行时缺失 | 浏览器区域无法初始化 | 启动时检测 `CoreWebView2Environment.GetAvailableBrowserVersionString()`，缺失则提示安装 Evergreen Runtime |
| 站点使用 blob: + MSE 播放 | 拿不到可下载的直链 | JS Hook 捕获 `addSourceBuffer` 的初始化片段与前缀 URL，尽力还原 |
| m3u8 需要动态 token | 嗅探到的地址短时失效 | 引导用户「立即下载」，并在 403 时重新从浏览器会话刷新 `RequestContext` |
| ffmpeg stderr 格式随版本变化 | 进度解析失效 | 解析器做宽松匹配 + 脏行容错，无法解析时降级为「不显示百分比」而非崩溃 |
| 大文件 mp4 多线程占用带宽 | 下载慢 | 并发片数可配置（默认 4），并提供单线程回退 |
| WinForms 高频进度更新卡 UI | 界面卡顿 | 节流刷新 + 虚拟化列表 |
