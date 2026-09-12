# HLS 下载改造设计：C# 取分片，ffmpeg 只做合并

- 日期：2026-09-13
- 状态：待评审
- 相关实测报告：见本文「二、实测证据」；验证装置位于 `%TEMP%\bvgb-harness`（一次性程序，不入库）

---

## 一、要解决的问题

用户提出的方向：**把「下载文件」交给 C#，只在最后合并成 mp4 时调用 ffmpeg。**

这个方向不只是"更干净"，而是修掉当前一批真实缺陷的前提条件。当前的 `FfmpegDownloadHandler`
把 ffmpeg 当作"网络客户端 + 解复用器 + 封装器"三合一使用，由此产生的问题全部集中在
"ffmpeg 与网络交互"这一段，而这一段恰恰是不可控、不可观测、无法逐片校验的。

### 现状职责（一条命令干三件事）

```
ffmpeg -headers "..." -protocol_whitelist ... -allowed_extensions ALL \
       -i https://cdn/playlist.m3u8 -c copy out.mp4
        └─ 取清单 ─┘└─ 取分片 ─┘└─ 解密 ─┘└─ 解复用 ─┘└─ 封装 ─┘
```

### 目标职责（各管一段，边界清晰）

```
C#  ：取清单 → 选清晰度 → 并发取分片 → 逐片校验 → AES-128 解密 → 按序拼接 → assembled.ts
ffmpeg：-i assembled.ts -c copy -movflags +faststart out.mp4
        └─ 纯本地文件的重封装，不碰网络 ─┘
```

---

## 二、实测证据

全部结论均来自对用户提供地址的真实抓取，不是推断。

测试地址（`verify` 令牌有效期至 2026-09-12 23:22:03）：

```
https://m3cdn.playergo.top/zA0DBqesvdDiDYVb-KTusS4ej6waCV5BX60SxobF-KJwWAiNp6UxEs83gSA/46059518b63269/playlist.m3u8?verify=1789226523-wVirk8W8YNZpm3kvsD9z8jUS_9AsanOLS2yf1qf96WE
```

清单结构：主清单 2 档清晰度（640x360 / 842x480）；媒体清单 1331 个分片，VOD，**无 DRM、无 AES**；
分片扩展名是 `.jpeg`，但内容是 MPEG-TS（`Content-Type: video/mp2t`）。

### 证据 1：`.jpeg` 扩展名会让 ffmpeg 直接拒绝（HLS 解复用器硬编码白名单）

```
URL .../842x480/video0.jpeg is not in allowed_segment_extensions,
consider updating hls.c and submitting a patch to ffmpeg-devel
Error opening input: Invalid data found when processing input
```

`-allowed_extensions ALL` **对它无效**。可行开关是 `-extension_picky 0`（实测退出码 0，产物正常）。
一个扩展名，就这么大一个坑。

### 证据 2：请求头 `Origin` 会让 CDN 对**每个**分片返回同一张占位图

同一地址、同一时刻，仅改变请求头：

| 请求头组合 | 返回 | 大小 | 分片哈希 |
|---|---|---|---|
| UA + Referer + **Origin** | `image/jpeg` | 56024 | 全部相同 |
| UA + Referer + Origin + Accept | `image/jpeg` | 56024 | 全部相同 |
| **UA** | `video/mp2t` | 586560 / 810280 | **互不相同** |
| **UA + Referer** | `video/mp2t` | 586560 / 810280 | **互不相同** |

`Origin` 存在 → 占位图；`Origin` 缺失 → 真实分片。2 个分片 × 4 种组合，全部一致。

**而我们两条链路都在发 `Origin`**：

- `RequestContext.BuildHeaderBlock()` 第 94 行 `AppendHeader("Origin", Origin)` → 进 ffmpeg `-headers`
- `HttpRequestHeaders.Apply()` 第 61–64 行 → 进 HttpClient 请求
- `WebView2RequestContextProvider` 第 88 行无条件设置 `context.Origin = $"{pageUri.Scheme}://{pageUri.Authority}"`

**后果**：ffmpeg 拿到 N 张完全相同的合法 JPEG，`-c copy` 顺利封装，退出码 0，
产物是一个"能打开、扩展名正常、但内容不是视频"的 mp4。这解释了"下载不完整"。

### 证据 3：占位内容带 HTTP 200，且按索引固定出现

40 个分片下载结果（并发 8 与并发 1 **逐字节相同**，与代理无关，`curl --noproxy` 结果一致）：

```
索引 00–21：26 个真实分片（261KB–900KB，互不相同）
索引 22–27：6 个 → 同一张 56024 字节 image/jpeg
索引 28–31：4 个真实分片
索引 32–39：8 个 → 同一张 56024 字节 image/jpeg
```

全片抽样（每 50 个取一个）：索引 0 / 100 为真实分片，索引 50 与 150 以后**全部为占位图**。

**关键推论**：HTTP 状态码 200 与 `image/jpeg` 占位内容无法在协议层区分于正常响应。
**只有逐片校验内容，才能发现"下载成功但内容无效"。**

### 证据 4：C# 取分片 + ffmpeg 本地合并，链路可用

| 指标 | 带 Origin（现状） | 不带 Origin（修复后） |
|---|---|---|
| 40 分片总大小 | 2.14 MB | **16.67 MB** |
| mp4 分辨率 | 1280x720（错） | **842x474**（与清单 842x480 一致） |
| 平均码率 | 27 kbps | **596 kbps** |
| 分片下载耗时 | 11.0s（并发 8） | **20.4s** |
| ffmpeg 合并耗时 | 0.3s | **0.4s** |
| ffmpeg 退出码 | 0 | **0** |
| ffprobe 校验 | h264 + aac | **h264(842x474) + aac，时长 219.5s** |

ffmpeg 只处理本地文件时耗时 0.4s、且无任何协议白名单 / 扩展名 / 代理相关问题。
`.jpeg` 分片、系统代理（`http_proxy=127.0.0.1:58553`）在这条路径上全部不再是问题。

### 证据 5：占位内容是一段"格式合法的 TS"，且一旦入缓存客户端无法自救

对 56024 字节的占位内容做 `ffprobe`：`format=mpegts`、8.023222 s、55861 bit/s、
h264 **1280x720** 25 fps、aac 44100 Hz stereo，**首字节为 `47`（合法 TS 同步字）**。

| 推论 | 说明 |
|---|---|
| 同步字校验无效 | 占位片带合法 `0x47`，按"检查 TS 同步字"实现的校验会**放行** |
| ffmpeg 无法发现 | ffmpeg 能正常解码这段 TS，退出码 0 ⇒ 工具判定"下载成功" |
| 时长错位 | 占位片恒 8.000 s，而清单声明 5.000 s / 2.500 s ⇒ 拼接后音画必然错位 |
| **无法绕过** | 实测 10 种手段（随机 query、`no-cache`、`no-store`、`Pragma`、`Range`、`./` 路径、`//` 路径、绕代理、换 UA、换 head）**全部命中同一条缓存诱饵**，`cf-cache-status: HIT`，`max-age` 31 天 |

同一个视频内的占位分布（旧地址 `…/46059518b63269/640x360`，共 1331 片 / 88.72 分钟）：
按 `Range: bytes=0-0` 抽样 56 个点 → **真实 41 / 占位 15（26.8%）**；
占位片集中在片头（0–5 连片）与片尾（900–1300 连片），中间零星出现。

**关键推论**：占位污染**不可能通过重试解决**，检测到之后必须如实报告，而不是把重试次数烧光。

### 证据 6：真实流是 AES-128 加密的 HLS（新增硬性需求）

同批次测得的 `as.oolrvd.cn/videos5/{hash}/crypt.key?auth_key=…` 返回 **200 + 16 字节密钥**。
`auth_key=<时间戳>-<随机数>-<uid>-<md5>` 是腾讯云 CDN TypeA 签名（签名与路径绑定），
因此只对该条 key 有效，同域其它路径一律 403。

**结论**：AES-128 解密不是"未来可能需要"，而是真实站点已经在用的能力，
必须作为一等需求落进 C# 取片链路（见 §三 的 `Aes128Decryptor`）。

---

## 三、方案

### 3.1 新增组件（Core 层，全部可单测）

| 组件 | 职责 |
|---|---|
| `IMediaFetcher` | 按字节取资源（取文本 / 取到文件 / 探测头部）。抽象出来是为了单测不联网 |
| `HttpMediaFetcher`（Infrastructure） | `IMediaFetcher` 的 HttpClient 实现，注入 `RequestContext` 请求头 |
| `HlsDownloadPlan` | 解析结果 → 下载计划的纯模型（清晰度、分片序列、密钥、init 段、是否直播） |
| `HlsPlanBuilder` | 由 `M3u8Playlist` + 主/媒体清单关系构建 `HlsDownloadPlan`；DRM 预检 |
| `SegmentContentValidator` | 逐片校验：Content-Type、TS 同步字节、大小合理性、重复指纹 |
| `Aes128Decryptor` | AES-128-CBC 解密（IV 取清单声明，缺省用媒体序号） |
| `HlsSegmentDownloader` | 并发取分片到 `.partN`，逐片校验，失败重试与退避，进度上报 |
| `HlsAssembler` | 编排：取清单 → 建计划 → 取分片 → 解密 → 拼接 → 交给 ffmpeg → 清理 |

### 3.2 改动

| 文件 | 改动 |
|---|---|
| `HlsDownloadHandler`（新，Infrastructure） | 实现 `IDownloadHandler`，`CanHandle` = m3u8；内部用 `HlsAssembler` |
| `FfmpegDownloadHandler` | 收敛为**兜底/直播**用途：不再用于 VOD 的 m3u8 |
| `FfmpegArgumentBuilder` | 新增"本地输入"模式；补 `-extension_picky 0`；**去掉 `Origin` 请求头** |
| `HttpRequestHeaders.Apply` | **默认不发 `Origin`**（改为可选，默认关闭） |
| `RequestContext.BuildHeaderBlock` | 同上，默认不含 `Origin` |
| `WebView2RequestContextProvider` | 不再无条件设置 `Origin`（或仅记录不发送） |
| `DownloadHandlerFactory` | 解析顺序：`HlsDownloadHandler` → 原生 `HttpDownloadHandler`(mp4) → `FfmpegDownloadHandler`(兜底/直播) |

### 3.3 关键设计点

**并发与限流**：默认 4 路（当前 `HttpDownloadOptions.SegmentCount` 是 8）。分片小、数量多，
过高的并发容易触发源站风控；同时保留"整数倍退避重试"。

**逐片校验（本方案的核心价值）**：

1. 响应 `Content-Type` 不属于视频/二进制的类别 → 标记可疑（`image/*`、`text/*`）；
2. 首字节不是 TS 同步字 `0x47`（且声明为 ts 时）→ 标记可疑；
3. 同一份内容在**连续多个不同分片**上重复出现 → 标记可疑（占位图特征）；
4. 全部可疑分片占比超阈值 → 判定"源站返回了无效内容"，**失败并给出明确中文提示**。

第 3 条是关键：占位内容一定是"多个不同地址返回同一份字节"，这是最可靠的特征。

**断点续传**：分片以 `.partN` 落盘，重跑时已校验通过的分片直接跳过。
优于现状——ffmpeg 直接写最终路径，中断即留下损坏的成品名文件。

**取消与清理**：取消时删除 `.partN` 与临时拼接文件，**最终 mp4 只在全部成功后由 ffmpeg 生成**，
不会出现"文件名正常、内容损坏"的产物。

**进度**：由分片下载完成数驱动（精确到字节），不再依赖解析 ffmpeg stderr 的 `-progress`。
合并阶段单独报一个"正在合并"状态。

**AES-128**：C# 侧用 `System.Security.Cryptography.Aes` 实现，密钥从 `#EXT-X-KEY` 的 URI 取。
SAMPLE-AES / Widevine / PlayReady 仍然**明确不支持**，在下载开始前就失败并说明原因。

**直播流**（清单无 `#EXT-X-ENDLIST`）：分片列表无终点，C# 侧需要轮询清单。
**建议本期不做**，直播仍走 ffmpeg 直连录制路径（`FfmpegDownloadHandler`）。

---

## 四、取舍

| 项目 | 说明 |
|---|---|
| 收益 | ① 修掉 `Origin` 导致的占位内容问题（现状无法在 ffmpeg 侧发现）② 逐片校验，可识别"假成功" ③ 取消/中断不留损坏成品 ④ 进度精确 ⑤ 断点续传 ⑥ 扩展名无关 |
| 代价 | 新增约 600 行实现 + 用例；AES-128 解密需自行实现与验证；多一个临时目录的磁盘占用（约为视频大小） |
| 不变 | ffmpeg 仍是硬依赖（合并环节）；DRM 内容仍不支持 |
| 风险 | 分片格式异常（非常规 TS）时校验可能误判 → 阈值与"可疑但放行"策略需可调 |

---

## 五、分阶段实施

1. **P0 修复（立即，与架构无关）**：去掉 `Origin` 请求头；ffmpeg 参数补 `-extension_picky 0`。
   这两条能立刻改善现状，且不引入新结构。
2. **P1 引入 C# 取分片**：`IMediaFetcher` / `HlsDownloadPlan` / `HlsSegmentDownloader` / `HlsAssembler`
   （含 AES-128、校验、续传、清理）+ 单元测试。
3. **P2 接入**：`HlsDownloadHandler` 注册进 `DownloadHandlerFactory`，ffmpeg 侧改为本地输入模式。
4. **P3 验证**：用真实地址做端到端验证；补 README 冒烟用例。

---

## 六、待确认

1. 是否按本方案重构（P1–P3），还是先只做 P0 的两处修复？
2. 校验判定为"占位/无效内容"时：立即失败并提示，还是先自动重试若干次再失败？
   （**证据 5 已给出答案**：占位一旦进 CDN 缓存就绕不过去，重试无意义 ⇒ 应当"检测到即失败并如实报告"。）
3. 是否保留 ffmpeg 直连（`-i URL`）作为兜底路径？保留则需同时应用 P0 的两处修复。
4. 直播流本期是否只保留 ffmpeg 录制路径（不做 C# 轮询）？
5. **新增**：占位污染无法移除时，是否接受"部分成功"（好分片拼成 mp4 + 在结果中标注缺失时间段）？

> 详细实测证据与复现步骤另见 `docs/review/2026-09-13-playergo-decoy-diagnosis.md`。

---

## 附：复现验证装置

验证程序位于 `%TEMP%\bvgb-harness`（该目录已被系统临时清理删除，需要时按下表重建），
复用生产代码（`M3u8Parser` / `VideoFamilyIndex` / `FfmpegArgumentBuilder` / `ProcessRunner` / `FfmpegLocator`），支持模式：

| 模式 | 用途 |
|---|---|
| `probe` | 解析清单、识别判定、家族折叠 |
| `reqmatrix` | 请求头组合对照（定位 `Origin` 问题） |
| `assemble` | C# 取分片 + ffmpeg 合并；参数 `origin` / `seq` 可切换对照组 |

例：`bvgb-harness.exe assemble <m3u8地址> <输出mp4> 40 seq`
