# BrowserVideoGrabber · 项目长期记忆

## 项目定位

WinForms 桌面工具：内置 WebView2 浏览器 + 页面视频嗅探 + 多格式视频下载。
布局为「左浏览器 / 右上嗅探 / 右下下载（待下载 / 正在下载 / 已下载）」。

## 硬性约定（不可违反）

1. **每个 `.cs` 文件必须带完整中文头注释**，模板为 RiverLand/LuBan 格式：
   `Copyright` / `CLR版本` / `机器名称` / `公司名称` / `命名空间` / `文件名` / `版本号` /
   `唯一标识`（唯一 GUID）/ `当前的用户域` / `创建人` / `电子邮箱` / `创建时间` / `描述`，
   后接 `修改标记` 段（`修改时间` / `修改人` / `版本号` / `描述`）。
2. 代码注释用中文，XML 文档注释完整；`<remarks>` 说明「为什么这么做」而非重复「做了什么」。
3. `Copyright` 年份用 **2026 RiverLand**，`创建人` 固定 `yswenli`。

## 技术栈与关键决策

| 项 | 决策 |
|---|---|
| 框架 | .NET 10 WinForms（`net10.0-windows`），`LangVersion=latest`、`Nullable=enable`、`ImplicitUsings=enable` |
| 浏览器内核 | WebView2（`Microsoft.Web.WebView2`，net10.0-windows 下需抑制 `MSB3277`） |
| 嗅探 | 网络响应监听 + URL 特征匹配 + JS Hook 注入三路互补；分片按目录折叠避免刷屏 |
| 下载 | `m3u8/ts/m4s/mpd` → ffmpeg；`mp4` → 原生 HTTP 多线程分片 + `.partN` 断点续传，失败回退 ffmpeg |
| ffmpeg | **不分发二进制**，运行时探测：设置路径 → 应用目录 `tools\ffmpeg` → 应用目录 → PATH → 常见安装位置 |
| DRM | AES-128 支持；SAMPLE-AES / SESSION-KEY 直接判失败（不可重试），不绕过 DRM |
| DI | 手写组合根 `AppHost`，不引入容器（YAGNI） |

## 分层与职责边界

```
App ──▶ Infrastructure ──▶ Core
```

- **Core**：零 UI / 零平台依赖，单元测试主战场（100 个用例，不联网、不触盘、不起真实进程）。
- **Infrastructure**：真实进程 / 磁盘 / WebView2 交互。
- **App**：仅渲染与事件绑定。
  - 面板只抛**意图**（`DownloadAction` 枚举 + 单一事件），不直接操作队列。
  - `MainForm` 是唯一把用户意图翻译成队列操作的地方。
  - 绑定器只同步数据、不做决策；跨线程封送与 200ms 进度节流都在绑定器里。
- `DownloadQueue` 是**唯一**允许改写 `DownloadTask.Status` 的地方。

## 构建与测试

```powershell
dotnet build BrowserVideoGrabber.slnx -c Release
dotnet test  BrowserVideoGrabber.slnx
```

## 本机环境注意事项

- **bash 不可用**：`ls` / `tail` / `grep` 等 coreutils 缺失，统一用 PowerShell。
- **PowerShell 标准输出常捕获不到**：把结果写入 `$env:TEMP\*.txt`，再用 Read 工具读取。
- **`Remove-Item` 被安全删除钩子拦截**（`SAFE_DELETE_FAIL_CLOSED`）：需要「清空文件」时改为
  记录文件长度差或改名，不要尝试删除。
- 控制台中文显示为乱码属编码问题，不影响实际写入内容（写入均为 UTF-8）。

## 已知易踩的坑

- WinForms `SplitContainer` 的 `Panel1MinSize`/`Panel2MinSize` **不能在初始化器里设置**，
  顺序必须是：解除最小尺寸 → 设 `SplitterDistance` → 施加最小尺寸。
- `HttpHeaders` 直接枚举会**重新解析已知头**（如 `User-Agent` 被拆成产品记号），
  取原始值要用 `request.Headers.NonValidated`。
- 下载超时**不要用 `HttpClient.Timeout`**（那是总时长，会误杀大文件），
  应使用「空闲超时」语义；同时必须关闭 `AutomaticDecompression`，否则分片拼接错位。
