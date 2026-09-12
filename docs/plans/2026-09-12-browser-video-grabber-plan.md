# BrowserVideoGrabber 实现计划

- **文档版本**：V1.0.0.0
- **创建时间**：2026/9/12 22:08:00
- **创建人**：yswenli
- **关联设计**：`docs/plans/2026-09-12-browser-video-grabber-design.md`

> 执行方式：**逐任务 TDD**。每个任务遵循 `写失败测试 → 跑测试确认红 → 写最小实现 → 跑测试确认绿 → 提交`。
> 任务粒度控制在 2~5 分钟可完成。

---

## 任务总览

| # | 任务 | 类型 | 依赖 |
|---|---|---|---|
| T1 | 解决方案骨架与 NuGet 依赖 | 基础设施 | — |
| T2 | Core 领域模型（Models） | 代码 | T1 |
| T3 | Core 抽象接口（Abstractions）+ Common | 代码 | T2 |
| T4 | `VideoUrlMatcher` 嗅探匹配 | **TDD** | T3 |
| T5 | `M3u8Parser` 播放列表解析 | **TDD** | T3 |
| T6 | `FfmpegArgumentBuilder` 命令构建 | **TDD** | T3 |
| T7 | `FfmpegProgressParser` 进度解析 | **TDD** | T3 |
| T8 | `DownloadQueue` 队列调度 | **TDD** | T4–T7 |
| T9 | Infrastructure · 进程与存储 | 代码 | T3 |
| T10 | Infrastructure · 下载处理器 | 代码 | T8、T9 |
| T11 | Infrastructure · WebView2 嗅探与 JS 注入 | 代码 | T3 |
| T12 | App · 三块面板与绑定 | 代码 | T10、T11 |
| T13 | App · 主窗体装配与设置 | 代码 | T12 |
| T14 | 构建、测试、冒烟与收尾 | 验证 | T13 |

---

## T1 · 解决方案骨架与 NuGet 依赖

**产出**

- `BrowserVideoGrabber.sln`
- `Directory.Build.props`（统一 `LangVersion=latest`、`Nullable=enable`、`ImplicitUsings=enable`、程序集信息）
- `src/BrowserVideoGrabber.Core`（`net10.0`）
- `src/BrowserVideoGrabber.Infrastructure`（`net10.0-windows`，PackageReference：`Microsoft.Web.WebView2`）
- `src/BrowserVideoGrabber.App`（`net10.0-windows`，`WinExe`，`UseWindowsForms=true`，PackageReference：`Microsoft.Web.WebView2`）
- `tests/BrowserVideoGrabber.Core.Tests`（`net10.0`，xUnit + `Microsoft.NET.Test.Sdk`）

**验收**：`dotnet build` 全解决方案成功；`dotnet test` 可运行（0 个测试通过亦可）。

---

## T2 · Core 领域模型

**文件**：`Models/VideoFormat.cs`、`SniffedVideo.cs`、`RequestContext.cs`、`DownloadStatus.cs`、`DownloadTask.cs`、`DownloadProgress.cs`、`DownloadResult.cs`

**要点**

- `VideoFormat`：`Unknown / M3u8 / Ts / M4s / Mp4 / Mpd`
- `DownloadStatus`：`Pending / Running / Paused / Completed / Failed / Canceled`
- `DownloadTask` 用 `record`，`Init` 属性 + 可变运行时状态；`Id` 用 `Guid`
- 全部带完整中文文件头注释 + XML 文档注释

---

## T3 · Core 抽象接口与 Common

**文件**：`Abstractions/*.cs`（5 个接口）、`Common/Result.cs`、`Common/RetryPolicy.cs`

**要点**

- 接口签名严格对齐设计文档第 5 节
- `RetryPolicy`：指数退避，`Delay(attempt) = baseDelay * 2^(attempt-1)`，提供 `ShouldRetry(attempt)`

---

## T4 · `VideoUrlMatcher`（TDD）

**测试先行**：`tests/.../Sniffing/VideoUrlMatcherTests.cs`

| 用例 | 期望 |
|---|---|
| `https://a.com/x/index.m3u8` | `M3u8` |
| `https://a.com/x/seg.ts?token=1` | `Ts`（query 不干扰） |
| `https://a.com/v.mp4#t=10` | `Mp4`（fragment 剥离） |
| `https://a.com/d/init-stream0.m4s` | `M4s` |
| `https://a.com/d/manifest.mpd` | `Mpd` |
| `https://a.com/INDEX.M3U8` | `M3u8`（大小写不敏感） |
| `https://a.com/a.jpg` | `Unknown` 且不入列表 |
| 同一 URL 重复上报 | 去重，只保留一条 |
| Content-Type 为 `application/vnd.apple.mpegurl` | 判为 `M3u8` |

**实现**：`TryMatch(string url, string? contentType, out VideoFormat)`、`Normalize(string url)`

---

## T5 · `M3u8Parser`（TDD）

**测试先行**：`tests/.../Downloads/M3u8ParserTests.cs`

| 用例 | 期望 |
|---|---|
| 普通媒体播放列表 | 分片数、总时长（`#EXTINF` 累加） |
| 主播放列表含多档 `#EXT-X-STREAM-INF` | 提取 `BANDWIDTH` / `RESOLUTION` 变体列表 |
| `#EXT-X-KEY:METHOD=AES-128,URI="k"` | `Encryption = Aes128`，`KeyUri` 正确 |
| `#EXT-X-KEY:METHOD=SAMPLE-AES` | `Encryption = Drm`，标记为受保护 |
| `#EXT-X-MAP:URI="init.mp4"` | `InitSegmentUri` 正确 |
| 相对路径分片 | 基于 playlist base URI 解析为绝对地址 |
| 空 / 畸形内容 | 不抛异常，返回空结果 + `IsValid = false` |

**实现**：`M3u8Playlist` 结果模型 + `M3u8Parser.Parse(string content, Uri baseUri)`

---

## T6 · `FfmpegArgumentBuilder`（TDD）

**测试先行**：`tests/.../Ffmpeg/FfmpegArgumentBuilderTests.cs`

| 用例 | 期望 |
|---|---|
| 基础 m3u8 | 含 `-i <url>`、`-c copy`、`-y`、输出路径 |
| 带 `RequestContext` | 生成 `-headers` 且含 `Referer:` / `User-Agent:` / `Cookie:` |
| 多行 header | 使用 `\r\n` 分隔且整体正确转义为一个参数 |
| m4s / ts | 附 `-allowed_extensions` 或 `-protocol_whitelist` 等必要参数 |
| 输出路径含空格 | 作为单个参数传递，不被拆分 |
| 追加 `-progress pipe:2` 或等价进度开关 | 存在 |

**实现**：`Build(DownloadTask task, FfmpegOptions options) → IReadOnlyList<string>`

---

## T7 · `FfmpegProgressParser`（TDD）

**测试先行**：`tests/.../Ffmpeg/FfmpegProgressParserTests.cs`

| 用例 | 期望 |
|---|---|
| stderr 出现 `Duration: 00:20:05.00` | 记录总时长 1205s |
| 后续 `time=00:12:41.00` | 百分比 ≈ 63.2% |
| 多行增量喂入 | 逐行解析，进度单调递增 |
| 脏行 / 无关行 | 忽略且不抛异常 |
| 无 `Duration` 只有 `time=` | 百分比降级为 0（未知总量），但仍上报已处理时长 |
| `speed=2.1x` | 解析出速度 2.1 |

**实现**：`FfmpegProgressParser.Feed(string line) → DownloadProgress?`

---

## T8 · `DownloadQueue`（TDD）

**测试先行**：`tests/.../Downloads/DownloadQueueTests.cs`（注入 `FakeProcessRunner`）

| 用例 | 期望 |
|---|---|
| 入队 5 个任务，并发上限 3 | 同时 running ≤ 3 |
| 任务成功 | 状态 `completed` 且移入已完成集合 |
| 任务失败且未超重试上限 | 状态回到 `pending` 并重试；超过上限则 `failed` |
| 取消运行中任务 | 状态 `canceled`，对应 CTS 被触发 |
| 单任务失败不影响其他任务 | 其余任务正常完成 |
| 暂停 / 恢复 | `running → paused → pending → running` |
| 进度事件 | 上报的百分比单调不减 |

**实现**：`DownloadQueue.EnqueueAsync` / `Start` / `Pause` / `Cancel` + `SemaphoreSlim` 并发控制

---

## T9 · Infrastructure · 进程与存储

**文件**：`Process/ProcessRunner.cs`、`Storage/JsonTaskRepository.cs`、`FfmpegLocator`（若放 Core 则归 T3）

**要点**

- `ProcessRunner`：`RedirectStandardError = true`、`StandardOutputEncoding = UTF8`、逐行回调、`Kill(entireProcessTree: true)`
- `JsonTaskRepository`：`System.Text.Json`，写入 `%LOCALAPPDATA%\BrowserVideoGrabber\tasks.json`，原子写（临时文件 + 替换）

---

## T10 · Infrastructure · 下载处理器

**文件**：`Downloads/HttpDownloadHandler.cs`、`Downloads/FfmpegDownloadHandler.cs`

**要点**

- `HttpDownloadHandler`：`HttpClient` + `Range` 分片（默认 4 并发）、断点续传（`.part` + `.progress`）、`RequestContext` 注入请求头、失败回退 ffmpeg
- `FfmpegDownloadHandler`：调用 `IProcessRunner` 跑 ffmpeg，接 `FfmpegProgressParser`，`-c copy` 输出 mp4，检测到 DRM 时直接失败并给中文提示

---

## T11 · Infrastructure · WebView2 嗅探与 JS 注入

**文件**：`Sniffing/NetworkSniffRules.cs`、`JsHookScript.cs`、`JsHookInjector.cs`、`WebView2Sniffer.cs`、`Security/CookieExporter.cs`

**要点**

- `JsHookScript`：ES5 兼容的 IIFE 字符串，劫持 `XMLHttpRequest.open` / `fetch` / `MediaSource.addSourceBuffer`，命中则 `chrome.webview.postMessage({type:'sniff',url:...})`
- `WebView2Sniffer`：订阅 `WebResourceResponseReceived`、`WebMessageReceived`，统一交给 `VideoUrlMatcher` 过滤，触发 `VideoDetected`
- `CookieExporter`：`CookieManager.GetCookiesAsync(url)` → `Cookie: k=v; k2=v2`

---

## T12 · App · 三块面板与绑定

**文件**：`Panes/BrowserPane.cs`、`SniffPane.cs`、`DownloadPane.cs`、`Binding/SniffListBinder.cs`、`DownloadListBinder.cs`

**要点**

- `BrowserPane`：ToolStrip（后退 / 前进 / 刷新 / 地址框 / 转到 / 嗅探开关）+ `WebView2` 填充
- `SniffPane`：`ListView`（Details：格式 / 分辨率 / 地址），右键菜单「加入下载」「复制链接」，双击直接下载
- `DownloadPane`：`TabControl` 三页签，各自 `ListView`；正在下载页含进度列
- `DownloadListBinder`：200ms 节流刷新，避免 UI 卡顿

---

## T13 · App · 主窗体装配与设置

**文件**：`Program.cs`、`AppHost.cs`、`MainForm.cs`、`Dialogs/SettingsForm.cs`、`AppSettings.cs`

**要点**

- 布局：`SplitContainer`（左 BrowserPane）+ 右侧 `SplitContainer`（横切 SniffPane / DownloadPane）
- `AppHost`：手写组合根，装配 sniffer / handlers / queue / repository
- `SettingsForm`：ffmpeg 路径（含「浏览」与「自动探测」）、输出目录、并发上限、HTTP 分片数
- ffmpeg 缺失时主窗体顶部横幅警告

---

## T14 · 构建、测试、冒烟与收尾

1. `dotnet build -c Release` 零错误；
2. `dotnet test` 全绿；
3. 编写 `README.md`（含 ffmpeg 安装说明、手工冒烟清单）；
4. 逐项核对「所有 `.cs` 文件均含完整中文头注释」；
5. 提交。
