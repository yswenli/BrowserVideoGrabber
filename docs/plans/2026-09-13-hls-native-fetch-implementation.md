# HLS 原生取片改造 · 实施计划

- 日期：2026-09-13
- 状态：执行中
- 上游设计：`docs/plans/2026-09-13-hls-native-fetch-design.md`（已评审通过）
- 实测报告：`docs/review/2026-09-13-playergo-decoy-diagnosis.md`

---

## 一、已拍板事项（来自设计文档「六、待确认」）

| 编号 | 事项 | 结论 |
|---|---|---|
| 1 | 是否按方案重构 | **是**，P0 → P3 全量实施 |
| 2 | 遇到占位/无效分片 | **不重试**，检测到即跳过并计入缺失 |
| 3 | 是否保留 ffmpeg 直连兜底 | **保留**，用于直播与 C# 链路失败回退，并应用 P0 修复 |
| 4 | 直播流本期处理 | **不做 C# 轮询**，直播交给 ffmpeg 直连录制 |
| 5 | 占位无法移除时 | **接受「部分成功」**：可用分片拼成 mp4 + 标注缺失时间段 |

---

## 二、验收标准

1. `.jpeg` 扩展名的 TS 分片能被完整识别与下载（不再依赖 ffmpeg 白名单奇迹）。
2. 占位/重复内容被逐片识别，**不会**被当成正常分片写进成品。
3. 部分污染时产物可播放，且失败/完成说明里能读到「缺失 N 片，约 00:01:30–00:03:10」这类信息。
4. AES-128 加密的 HLS 能正确解密（密钥来自 `#EXT-X-KEY`，IV 取清单声明或媒体序号）。
5. 全仓库不再发送 `Origin` 请求头（该头会让 CDN 对每个分片返回同一张占位图）。
6. 主窗体只保留「正在下载」「已下载」两个页签；所有窗体图标统一为 `favicon.ico`；
   浏览器加载页面时界面有明确的加载提示。
7. `dotnet test -c Release` 全绿，构建 0 警告 0 错误。

---

## 三、任务分解

### T0 收尾与准备

- [x] 清理探测脚本残留（`.probe/`、根目录 `.probe_*`）
- [x] `.gitignore` 增加 `.probe/` 忽略
- [x] 设计文档「待确认」更新为「已确认结论」

### T1 P0 修复（与架构无关，先落地）

| 任务 | 内容 | 验证 |
|---|---|---|
| T1.1 | `FfmpegOptions` 增 `DisableExtensionPicky`（默认 true）、`EnableFastStart`（默认 true） | 单测断言参数存在 |
| T1.2 | `FfmpegArgumentBuilder` 流式分支输出 `-extension_picky 0` | `FfmpegArgumentBuilderTests` 新增用例 |
| T1.3 | `FfmpegArgumentBuilder.BuildLocalRemux(input, output, opts)`：本地文件重封装，不含网络相关开关 | 新增用例：无 `-protocol_whitelist`、无 `-headers`、含 `-c copy`、输出在末位 |
| T1.4 | 停止发送 `Origin`：`RequestContext.BuildHeaderBlock`、`HttpRequestHeaders.Apply`、`WebView2RequestContextProvider.CreateAsync` 三处 | 新增 `RequestContextTests` + `HttpRequestHeadersTests` 断言头块/请求头中无 `Origin` |

> `Origin` 属性本身**保留**（持久化兼容 + 便于诊断），只是默认不再外发。这一点必须写进注释，
> 否则后来者会以为「有属性却不用」是遗漏。

### T2 Core 层新增组件（全部可单测）

| 任务 | 新增文件 | 职责 |
|---|---|---|
| T2.1 | `Abstractions/IMediaFetcher.cs`、`Models/MediaFetchResult.cs` | 按字节取资源；结果携带状态码 / Content-Type / 长度 / 可重试性 |
| T2.2 | `Downloads/M3u8Playlist.cs`（扩展）、`Downloads/M3u8Parser.cs`（扩展） | 补 `KeyIv` / `MediaSequence` / `IsLive`（无 `#EXT-X-ENDLIST`） |
| T2.3 | `Downloads/HlsDownloadPlan.cs` | 下载计划纯模型：清晰度、分片序列、各片起始时间、密钥 URI/IV、init 段、直播标志 |
| T2.4 | `Downloads/HlsPlanBuilder.cs` | 媒体清单文本 → 计划；DRM 预检；直播识别 |
| T2.5 | `Downloads/SegmentContentValidator.cs` | 逐片判定：Content-Type、TS 同步字、大小合理性、重复指纹 |
| T2.6 | `Downloads/Aes128Decryptor.cs` | AES-128-CBC 解密；IV 缺省由媒体序号推导 |
| T2.7 | `Downloads/HlsSegmentDownloader.cs` | 并发取片 → 校验 → 解密 → 落 `.partN`；产出 `SegmentDownloadReport`（含缺失清单） |
| T2.8 | `Downloads/HlsAssembler.cs` | 按序拼接可用分片（含 init 段）为单个 `.ts` |

单测配套：`M3u8ParserTests`（扩展）、`HlsPlanBuilderTests`、`SegmentContentValidatorTests`、
`Aes128DecryptorTests`、`HlsSegmentDownloaderTests`、`HlsAssemblerTests`。

### T3 Infrastructure 接入

| 任务 | 内容 |
|---|---|
| T3.1 | `Downloads/HttpMediaFetcher.cs`：`IMediaFetcher` 的 HttpClient 实现 |
| T3.2 | `Downloads/HlsDownloadHandler.cs`：编排取清单 → 建计划 → 取片 → 解密 → 拼接 → ffmpeg 合并；直播/失败回退 ffmpeg |
| T3.3 | `DownloadHandlerFactory` 注册顺序：`HlsDownloadHandler` → `HttpDownloadHandler`(mp4) → `FfmpegDownloadHandler`(兜底) |
| T3.4 | `AppHost.CreateQueue` 装配新处理器；`HttpDownloadHandler` 的 fallback 指向 ffmpeg 保持不变 |

单测：`HlsDownloadHandlerTests`（假 fetcher + 假进程运行器 + 内存文件系统）。

### T4 「部分成功」贯通到界面

| 任务 | 内容 |
|---|---|
| T4.1 | `DownloadResult` 增 `IsPartial` / `PartialDetail`，新增 `OkPartial(...)` |
| T4.2 | `DownloadTask` 增同名字段并纳入 `Clone()` |
| T4.3 | `DownloadQueue.RunTaskAsync` 成功分支回写这两项 |
| T4.4 | `DownloadPane` 结果列展示部分成功说明；`DisplayText` 增「部分完成」文案 |

### T5 界面四项

| 任务 | 内容 |
|---|---|
| T5.1 | 新增 `App/AppIcon.cs`；`MainForm`、`SettingsForm` 统一设置 `Icon` |
| T5.2 | `DownloadPane` 移除「待下载」页签：`Pending` / `Paused` 归入「正在下载」页签，列结构统一为 5 列 |
| T5.3 | `BrowserPane` 加载提示器：工具栏下方 `Dock=Top` 提示条（跑马灯 + 文案），`NavigationStarting` 显示、`NavigationCompleted` 隐藏 |
| T5.4 | 深度审查：逐文件核对命名/注释/异常边界/生命周期一致性，产出审查报告 |

### T6 验收

- [ ] `dotnet test -c Release` 全绿
- [ ] `dotnet build -c Release` 0 警告 0 错误
- [ ] 全部新增代码文件带完整中文头部注释与唯一 GUID
- [ ] 用真实地址做一次端到端冒烟（地址须从页面重新嗅探，旧 `verify` 令牌已过期）

---

## 四、风险与对策

| 风险 | 对策 |
|---|---|
| `-extension_picky` 在 ffmpeg < 7.1 上会被判为未知参数 | 做成可配置项，默认开启；注释写明版本要求 |
| 丢弃中间分片导致时间戳跳跃 | 属可接受行为，但必须在说明列如实标注缺失区间 |
| 占位片与真实片大小差异过大时误判 | 校验以「跨片重复指纹」为主、Content-Type 与同步字为辅，任一命中即判可疑 |
| 内存占用：并发取片 + 校验需读首字节 | 分片落盘后以流式读取首 16 字节与末尾 16 字节做指纹，不整体载入内存 |
| C# 链路失败后回退 ffmpeg 可能重复下载 | 回退仅发生在「建计划失败/直播」与「分片全不可用」两类情形，量级可控 |
