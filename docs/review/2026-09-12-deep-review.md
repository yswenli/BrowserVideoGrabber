# BrowserVideoGrabber 深度审查报告

- **审查对象**：`BrowserVideoGrabber` 全部已提交代码（commit `777ddad`）
- **审查范围**：66 个手写 `.cs` 文件（Core / Infrastructure / App / Tests）、设计文档、README
- **审查方法**：逐文件阅读原文（非摘要），对照调用方交叉验证每条结论；对可编程验证的项直接跑脚本核对
- **审查日期**：2026-09-12

---

## 1. 结论摘要

| 级别 | 数量 | 含义 |
| --- | --- | --- |
| **P0 必须修** | 5 | 用户可感知的功能缺陷或数据/文件残留问题，建议在发布前修复 |
| **P1 建议修** | 11 | 逻辑不一致、文档与实现相反、健壮性缺口 |
| **P2 可延后** | 10 | 死代码、可优化项、取舍说明 |
| **架构建议** | 5 | 职责边界与测试覆盖的长期改善 |

**总体评价**：分层（Core 零 UI 依赖）与测试纪律（100 个测试全绿）在同类桌面小工具中属于上游水平，`HttpDownloadHandler` 的「每分片一个 `.partN`」续传设计、`M3u8Parser` 的「宽进严出」、`ProcessRunner` 的「stdout 必须抽干」这几处判断都体现了对真实故障模式的理解。

但审查发现：**所有缺陷集中在「UI 装配层」与「层与层的接缝」上**——恰好是唯一没有测试覆盖的区域。Core 与 Infrastructure 的单元测试质量很高，而 1500 行 UI 代码零测试，P0 级的 5 个问题里有 2 个直接落在这里。

---

## 2. P0 · 必须修

### H1 「已下载」页签的「说明」列与「正在下载」的「地址」列**永远空白**

**严重度**：高 ｜ **确认方式**：代码路径推演（可 1 分钟复现）

**位置**
- `App/Panes/DownloadPane.cs:259-269`（`CreateItem`）
- `App/Panes/DownloadPane.cs:77 / 80 / 83`（三个列表的列定义）
- `App/Controls/BufferedListView.cs:77-82`（`SetSubItemText` 的静默越界返回）
- `App/Panes/DownloadPane.cs:298`（写「说明」列）、`:324`（写「地址」列）

**根因**

三个页签的列数不同：

```csharp
// DownloadPane.cs:77,80,83
_pendingList.AddColumns(("标题",220),("格式",80),("状态",80),("地址",360));            // 4 列
_runningList.AddColumns(("标题",200),("进度",80),("速度",90),("已下载",130),("地址",360)); // 5 列
_finishedList.AddColumns(("标题",200),("结果",70),("大小",90),("完成时间",110),("说明",360)); // 5 列
```

而行对象只在**首次创建时**按「当时所属列表」的列数补齐子项：

```csharp
// DownloadPane.cs:259-269
private static ListViewItem CreateItem(BufferedListView list)
{
    var item = new ListViewItem(string.Empty);
    for (var index = 1; index < list.Columns.Count; index++)   // ← 按创建时所在列表的列数
        item.SubItems.Add(string.Empty);
    return item;
}
```

而 `SetSubItemText` 在越界时**静默返回**：

```csharp
// BufferedListView.cs:79-82
if (index < 0 || index >= item.SubItems.Count)
{
    return;      // ← 不抛异常、不记录、不提示
}
```

**触发链（100% 复现，不是边界情况）**

任务首次被界面感知时必然是 `Pending`（`MainForm.OnDownloadRequested` → `Enqueue` → `NotifyStateChanged(task, Pending)`），因此 `targetList = _pendingList`，行只有 **4 个子项**。此后：

| 迁移阶段 | 目标列表列数 | 写入索引 4 的结果 |
| --- | --- | --- |
| 待下载 → 正在下载 | 5 | 「地址」列 **空白** |
| 正在下载 → 已下载 | 5 | 「说明」列 **空白** |

**影响**

- 「已下载」页签的**「说明」列永远显示不出 `LastError`（失败原因）与 `OutputPath`（输出路径）**。这直接抹掉了 `FfmpegDownloadHandler.BuildErrorDetail` 与 `HttpDownloadHandler.ProbeResult.FromStatus` 里那些精心编写的中文提示（「请在浏览器中登录该站点后重新嗅探并下载」「资源地址已失效，通常为动态签名过期」）——用户看到的是空白列加一个「失败」。
- 讽刺的是：**从 tasks.json 恢复的历史任务显示正常**（它们在 `Rebuild` 时首建就直接落在 `_finishedList`，拿到 5 个子项）。所以这个 bug 只在「本次会话中新完成的任务」上出现，人工冒烟测试时极易被忽略或误判为偶发。

**建议修法**（两者取一）

```csharp
// 方案 A（推荐，改动最小）：填充前把子项补齐到目标列数
public void ApplyState(DownloadTask task)
{
    // ...
    else if (!ReferenceEquals(_hostListById[task.Id], targetList))
    {
        _hostListById[task.Id].Items.Remove(item);
        EnsureSubItemCount(item, targetList.Columns.Count);   // ← 新增
        targetList.Items.Add(item);
        _hostListById[task.Id] = targetList;
    }
    // ...
}

private static void EnsureSubItemCount(ListViewItem item, int columnCount)
{
    while (item.SubItems.Count < columnCount)
    {
        item.SubItems.Add(string.Empty);
    }
}

// 方案 B：CreateItem 按三个列表的列数上限（5）统一补齐
```

> 顺带建议：`SetSubItemText` 的静默返回是这个 bug 得以潜伏的根本原因。可考虑在 Debug 下 `Debug.Fail()`，或改成返回 `bool` 让调用方至少能察觉。**「静默吞掉越界」是一个应当被消除的坏味道，而不只是一个可以保留的防御。**

---

### H2 空闲超时（IdleTimeout）中断读流时被误判为「不可重试」，多分片下还会被静默吞掉

**严重度**：高 ｜ **确认方式**：代码路径推演

**位置**
- `Infrastructure/Downloads/HttpDownloadHandler.cs:559-578`（读流循环，`ReadAsync(..., idle.Token)` 在 `:561`）
- `Infrastructure/Downloads/HttpDownloadHandler.cs:635-639`（`IsTransient` 的类型集合）
- `Infrastructure/Downloads/HttpDownloadHandler.cs:461-478`（`DownloadSegmentAsync` 的两层 catch）
- `Infrastructure/Downloads/HttpDownloadHandler.cs:417-420`（worker 的 `catch (OperationCanceledException)`）

**根因**（三层叠加，缺一层都不会出问题）

1. 读流循环用的是 **`idle.Token`**（`:561`），空闲超时触发时抛出的是 `OperationCanceledException`；
2. `IsTransient` 只认四种类型：

```csharp
// HttpDownloadHandler.cs:635-639
private static bool IsTransient(Exception exception)
    => exception is HttpRequestException
        or TaskCanceledException     // ← 注意：OperationCanceledException 不是 TaskCanceledException
        or IOException
        or SocketException;
```

   `Stream.ReadAsync` 在令牌取消时抛的是 `OperationCanceledException`（基类），**不在集合内**；
3. `DownloadSegmentAsync` 的第二层 catch 因此不匹配（`:469` 要求 `cancellationToken.IsCancellationRequested`，而此处取消的是 `idle` 而非 `linked`；`:473` 要求 `IsTransient`），异常冒泡到 worker；
4. worker 把它当成「兄弟分片失败触发的快速失败」**直接吞掉**：

```csharp
// HttpDownloadHandler.cs:417-420
catch (OperationCanceledException)
{
    // 由外层统一判定是「用户取消」还是「兄弟分片失败触发的快速失败」
}
```

   → `firstError` 保持 `null` → `:435-443` 的两个判断都不成立 → `DownloadSegmentsAsync` **正常返回**。

**影响**

| 场景 | 实际后果 |
| --- | --- |
| 多分片 | 靠 `DownloadCoreAsync:283` 的「拼接长度比对」兜底，最终报 `isRetryable: true`；但**分片级重试完全失效**（本应就地重试 2 次），且报错原因是「数据不完整」而非「连接僵死」，误导排查方向 |
| 单连接 + 长度未知 | 无长度可比对 → 读到一半停滞，异常冒泡到 `DownloadAsync:180` 的兜底 catch → `Describe` 落到 `_ =>` 分支 → 用户看到 **「下载出错：操作已取消。」且 `isRetryable: false`**（永久失败，不重试） |

注意 `ProbeTimeout` 那一条路径是**正确**的——探测阶段 `SendAsync` 取消抛 `TaskCanceledException`，命中 `IsTransient`，测试 `DownloadAsync_ShouldFallback_WhenProbeTimesOut` 也覆盖了。**问题只出在「正文读取阶段」**，而这一段没有测试。

**建议修法**

从「异常类型」改为「取消来源」判定：

```csharp
// DownloadSegmentAsync 内
catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
{
    // 空闲超时：属于瞬时故障，走就地重试
    if (attempt >= _options.MaxRetryPerSegment) throw new IOException("连接长时间无数据传输（空闲超时）。");
    await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
}
```

同时 worker 的吞异常分支应加上「非 firstError 取消」的约束，避免掩盖真实故障。

---

### H3 暂停 / 取消存在竞态窗口，会被随后的「成功」覆盖

**严重度**：高（用户可见的错误状态）｜ **确认方式**：并发时序分析

**位置**
- `Core/Downloads/DownloadQueue.cs:490-511`（认领任务）
- `Core/Downloads/DownloadQueue.cs:551-554`（注册取消源）
- `Core/Downloads/DownloadQueue.cs:562-574`（成功分支无条件改写状态）

**根因**

「把状态改为 Running」与「把 CTS 放进 `_cancellations`」**不在同一个临界区**，中间隔着 `_factory.Resolve`、`CreateLinkedTokenSource` 与一次独立加锁：

```csharp
// 泵线程
lock (_gate) { if (next.Status == Pending) { next.Status = Running; claimed = true; } }   // :492-499
_ = RunTaskAsync(next, cancellationToken);                                                // :511

// RunTaskAsync 内部
handler = _factory.Resolve(task);                                  // :540
taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);  // :549
lock (_gate) { _cancellations[task.Id] = taskCancellation; }        // :551-554
```

若 UI 线程在这个窗口内调用 `Pause`/`Cancel`：状态被改成 `Paused`/`Canceled`，但 `_cancellations.TryGetValue` **取不到 CTS** → `CancelSafely(null)` 直接返回 → **取消动作完全落空**，处理器继续跑。跑完进入成功分支：

```csharp
// :562-574
if (result.Success)
{
    lock (_gate) { task.Status = DownloadStatus.Completed; ... }   // ← 无条件覆盖 Paused / Canceled
    NotifyStateChanged(task, DownloadStatus.Running);
    return;
}
```

**影响**：用户点了「取消」，任务却变成「已完成」并出现在已下载列表里；点「暂停」同理。窗口很窄（微秒级），但 UI 线程与泵线程是真并发，属于真实缺陷而非理论问题。

**建议修法**：把「认领 + 建 CTS + 注册」合并进同一个锁临界区；并给成功分支加一道防线：

```csharp
if (result.Success && !IsTerminal(task.Status) && task.Status != DownloadStatus.Paused)
{
    // 只有仍处于 Running 的任务才允许迁移到 Completed
}
```

---

### H4 调度泵退出与入队并发时，任务可能**永久停在「待下载」**

**严重度**：中高 ｜ **确认方式**：并发时序分析

**位置**
- `Core/Downloads/DownloadQueue.cs:476-479`（泵的退出判定）
- `Core/Downloads/DownloadQueue.cs:231-235`（`Start` 的 `_pump.IsCompleted` 判定）
- `Core/Downloads/DownloadQueue.cs:184-196`（`Enqueue` 不调用 `Start`）

**根因**

```csharp
// 泵线程                                                    // UI 线程
lock (_gate) { 扫描; allSettled = true; }
if (allSettled) return;            // ← 已决定退出
                                                             Enqueue(task);   // 新增 Pending
                                                             Start();         // _pump.IsCompleted == false → 不重启
// 泵返回 → _pump 完成。新任务无人拾取
```

`Start()` 判断的是 `_pump is null || _pump.IsCompleted`（`:231`），在泵「已决定退出但尚未完成」的窗口里两者都为假 → 不重启。泵随即返回，**新任务永远停在 Pending**。

**影响**：用户点了「加入下载」，界面显示在「待下载」页签且永不推进，无任何错误提示。自愈途径只有：再下载一个别的资源（会再次调用 `Start`），或对该任务做「暂停 → 恢复」（`Resume` 会调用 `Start`）。用户基本不会想到这两种操作。

**建议修法**：与其判断 `_pump.IsCompleted`，不如让泵在退出前**再确认一次**，或用「重启请求标志」：

```csharp
// PumpAsync 结尾
lock (_gate)
{
    // 退出前重新确认：若在判定后被塞入新任务，则继续循环而不是返回
    if (_tasks.Any(t => t.Status == DownloadStatus.Pending && 未在退避窗口内))
    {
        continue;
    }
    return;
}
```

---

### H5 取消 / 失败会在磁盘上留下**损坏的半成品文件**，且 `.partN` 孤儿无人回收

**严重度**：中高 ｜ **确认方式**：代码路径 + 两条链路对比

**位置**
- `Infrastructure/Downloads/FfmpegDownloadHandler.cs:127-145`（直接把 `task.OutputPath` 交给 ffmpeg 并带 `-y`）
- `Infrastructure/Downloads/HttpDownloadHandler.cs:170-175`（仅取消路径清理半成品）
- `Infrastructure/Downloads/HttpDownloadHandler.cs:176-183`（可重试失败 → 回退 ffmpeg，**不清理**）
- `Core/Downloads/DownloadQueue.cs:327-362`（`Remove` / `ClearFinished` 只删内存记录）

**问题清单**

| # | 场景 | 残留物 | 危害 |
| --- | --- | --- | --- |
| a | 原生下载可重试失败 → 回退 ffmpeg 并**成功** | `.partN` + `.assembling` | 每次回退都泄漏一份与视频等大的临时文件 |
| b | 原生下载**不可重试失败**（如 `Content-Range` 异常） | `.partN` | 任务已终态，永不再被引用 |
| c | 用户**取消** ffmpeg 任务 | `OutputPath` 处的**半截 `.mp4`** | **危害最大**：文件名完全正常、无任何标记，用户双击得到「文件损坏」，且会误以为是下载成功 |
| d | 任务被「从列表移除」或「清空已完成」 | 上述全部 | 磁盘被永久占用，界面上已无任何线索 |

注意两条链路的**不对称**：`HttpDownloadHandler` 精心设计了「用户取消清理全部 `.partN`」（注释与测试 `DownloadAsync_ShouldPropagateCancellation_AndCleanupParts` 都覆盖了），而 `FfmpegDownloadHandler` 完全没有对应机制——它把输出直接写到最终路径，取消时 ffmpeg 进程被杀，半截文件原地留下。

**建议修法**

1. ffmpeg 输出先写 `<OutputPath>.part`，`exitCode == 0` 且文件存在后再 `MoveFile` 改名（与原生下载器的 `.assembling` 策略对齐）；
2. 在 `HttpDownloadHandler` 的「回退成功」与「不可重试失败」两条路径上也调用 `Cleanup(artifacts)`；
3. `DownloadQueue.Remove/ClearFinished` 增加可选的磁盘清理钩子（或由 `AppHost` 订阅后处理），按 `OutputPath` 派生路径回收 `.part*` / `.assembling`；
4. README 的「已知限制」补一条说明。

---

## 3. P1 · 建议修

### M1 嗅探结果的两条链路「合并」逻辑**不可达**，分辨率列大概率长期为空

**位置**：`Core/Models/SniffedVideo.cs:37-38`、`Infrastructure/Sniffing/WebView2Sniffer.cs:359-394`、`App/Panes/SniffPane.cs:106-131`

`SniffPane.AddOrUpdate` 的注释声称：

> 同一条资源可能被网络监听与 JS 注入同时捕获，此时用更完整的一条覆盖（例如网络链路能给出分辨率而 JS 链路不能）

**这条路径实际永远不会执行**，原因有两重：

1. `SniffedVideo.Id { get; init; } = Guid.NewGuid()` —— 每次构造都是**全新 Guid**，`_itemsById.TryGetValue(video.Id)` 第二次传入时不可能命中；
2. 更根本的是：`WebView2Sniffer.Report` 已在**上报之前**用 `_fingerprints` 做了指纹去重（`:367-378`），同一资源第二次根本不会到达面板。

**后果**：
- `SniffPane.AddOrUpdate` 的更新分支与整个 `_itemsById` 索引都是死代码，它实际只是 `Add`；
- 哪个链路**先**报到就是哪个，纯看运气。若 JS Hook 先捕获到 `.m3u8`（脚本注入早于页面脚本执行，且它 hook 了 `XHR`/`fetch`），网络响应链路的报告会被指纹去重静默丢弃 → **「分辨率」列保持空白**，而这正是设计里想要修复的问题。

**建议**：把「合并」上移到嗅探器层——`Report` 在指纹命中时，用新结果**补全**已有记录（分辨率/码率/ContentType 取并集），再决定是否二次上报；或者把 `SniffedVideo.Id` 改为由指纹派生（如指纹的稳定哈希），让面板层的索引真正可用。

---

### M2 嗅探器达到 800 条上限后**永久静默失效**，且「清空列表」按钮的行为与提示相反

**位置**：`Infrastructure/Sniffing/WebView2Sniffer.cs:87 / 119-125 / 367-378`、`App/Panes/SniffPane.cs:73-78 / 136-141`

```csharp
// WebView2Sniffer.cs:367-378
lock (_gate)
{
    if (_fingerprints.Count >= MaxTrackedItems)   // 800
    {
        return;                                   // ← 只丢弃，不淘汰旧指纹
    }
    if (!_fingerprints.Add(fingerprint)) return;
}
```

而 `WebView2Sniffer.Clear()`（`:119-125`）**没有任何调用方**（已用 grep 全仓核实）。界面上的「清空列表」按钮只清面板自己的两个集合：

```csharp
// SniffPane.cs:136-141
public void ClearItems()
{
    _listView.Items.Clear();
    _itemsById.Clear();      // ← 不触碰 WebView2Sniffer._fingerprints
}
```

**后果（两个独立缺陷）**

1. 长会话浏览到约 800 条唯一资源后，嗅探**彻底停止上报且无任何提示**——开关仍显示「嗅探：开」，用户只会以为「这个网站抓不到」。
2. 「清空列表」按钮的 ToolTip 写着「清空后同一资源可以再次被捕获」，但指纹集合没清，**同一资源永远不会再被上报**。提示文案与实现相反。

**建议**：`_fingerprints` 改为 LRU／FIFO 淘汰（容量满时移除最旧一条）而非直接拒绝；`SniffPane` 通过事件通知上层调用嗅探器的 `Clear()`，或把 `Clear()` 提到 `IVideoSniffer` 接口上。

---

### M3 `AppHost.SaveNow()` 与防抖持久化**并发写同一个 `.tmp` 文件**

**位置**：`App/AppHost.cs:260-273`、`App/AppHost.cs:433-457`、`Infrastructure/Storage/JsonTaskRepository.cs:136`

`_persistGate` 只保护了定时器触发的 `PersistQuietlyAsync`；`SaveNow`（关闭程序时由 `MainForm` 调用）**绕过了这个门闩**：

```csharp
// AppHost.cs:267
Task.Run(() => Queue.PersistAsync()).Wait(TimeSpan.FromSeconds(5));
```

两者最终都落到 `JsonTaskRepository.SaveAsync`，而它用的是**同一个固定临时路径**：

```csharp
// JsonTaskRepository.cs:136
var temporaryPath = _filePath + ".tmp";
```

交错执行时会出现「A 写完 tmp → B 覆盖 tmp → A 把 tmp Move 走 → B 的 Move 找不到源文件」→ `FileNotFoundException`（继承自 `IOException`，会被 `PersistQuietlyAsync` 吞掉，但 `SaveNow` 的 catch 只覆盖 `AggregateException / ObjectDisposedException` → **异常从 `Wait()` 包装后逃逸**，被 `MainForm` 的 catch 拦下，静默）。

**后果**：退出时的最后一次落盘可能失败且无任何反馈，表现为「下次启动丢失最后几个任务状态」。

**建议**：`SaveNow` 复用 `_persistGate`；或给 `SaveAsync` 的临时文件名加上 `Guid` 后缀避免碰撞。

---

### M4 `JsHookInjector.InjectAsync` 是 fire-and-forget，注入失败无声

**位置**：`Infrastructure/Sniffing/WebView2Sniffer.cs:164`

```csharp
_ = JsHookInjector.InjectAsync(coreWebView);
Subscribe(coreWebView);
```

注入失败（WebView2 就绪竞态、脚本被企业策略拦截）时，JS 捕获链路**静默失灵**，只剩网络响应链路，而界面上完全看不出差别。建议 `await` 并 `try/catch` 落盘到 `StartupDiagnostics`。

---

### M5 `M3u8Parser` 的 `isDrmProtected` 一旦置位**永不复位**

**位置**：`Core/Downloads/M3u8Parser.cs:107-141`

`#EXT-X-KEY:METHOD=NONE` 会把 `encryption` 复位为 `None`（`:125`），但 `isDrmProtected` 只在 `:134` 被置 `true`，**没有任何复位路径**。若一份清单先出现 `SESSION-KEY`（或 `SAMPLE-AES`）随后声明 `METHOD=NONE`，`FfmpegDownloadHandler:114-121` 会据此直接判失败并提示「受 DRM 保护」。罕见但逻辑上明确不一致。建议在 `METHOD=NONE` 分支同步复位。

另：`KeyUri` 会被后续 `#EXT-X-KEY` 覆盖，密钥轮换场景只保留最后一个——注释已声明不支持，可以接受，但值得在 README 的已知限制里点名。

---

### M6 标题值未做 **CRLF 注入**防护

**位置**：`Core/Models/RequestContext.cs:78-104`、`Infrastructure/Downloads/HttpRequestHeaders.cs:56-74`

`BuildHeaderBlock` 把 `Referer / UserAgent / Origin / Cookie / ExtraHeaders` 直接拼进 `name + ": " + value + "\r\n"`。值中若含 `\r\n`，即可注入任意请求头。

其中 `ExtraHeaders` 来自 `tasks.json`（用户可写，同机其它进程也可改），`Cookie` 来自站点。更微妙的是 `HttpRequestHeaders.Apply` 全部使用 `TryAddWithoutValidation`（`:58 / 68 / 73`）——**恰好绕过了框架自带的头值校验**。

**建议**：在 `BuildHeaderBlock` 与 `Apply` 里统一剔除值中的 `\r` / `\n`（一处工具方法即可），成本极低。

---

### M7 分片下载不校验响应的 `Content-Range`

**位置**：`Infrastructure/Downloads/HttpDownloadHandler.cs:540-544`

```csharp
if (response.StatusCode == HttpStatusCode.OK && from > 0)
{
    throw new HttpRequestException("服务器未按 Range 返回分段数据，无法继续断点续传。");
}
```

只拦截了「应该给 206 却给了 200」。若服务端返回 **206 但区间错误**（CDN 配置不当、忽略 Range 但从 0 开始切片），代码会**先把错误字节写进分片**，再由 `:580-583` 的长度校验或拼接总长度校验发现，报「数据不完整」。

方向上是安全的（不会产出损坏的成品），但诊断信息会误导（用户以为网络不稳，实际是服务端不守规矩）。建议加一行比对：

```csharp
if (response.Content.Headers.ContentRange is { } range && range.From != from)
{
    throw new HttpRequestException($"服务器返回的分段区间与请求不符（请求 {from}，返回 {range.From}）。");
}
```

---

### M8 `WaitForCompletionAsync` 的 XML 注释与实现**完全相反**

**位置**：`Core/Downloads/DownloadQueue.cs:370`（注释）vs `:381`（实现）

```csharp
/// <remarks>处于暂停状态的任务不算落定，会导致本方法一直等待；调用方需自行避免该情况。</remarks>
```

```csharp
settled = _tasks.All(t => t.Status != DownloadStatus.Pending && t.Status != DownloadStatus.Running);
//   ↑ Paused 既不是 Pending 也不是 Running → 被判定为「已落定」→ 立即返回
```

实现是对的（暂停也应当算落定），**注释是错的**。这属于会主动误导后续维护者的文档缺陷，建议直接改注释。测试 `Should_PauseThenResumeTask` 里 `Resume` 后等待完成的写法恰好掩盖了这一点。

---

### M9 「进度单调不减」的承诺在 ffmpeg 链路上不成立

**位置**：`Core/Downloads/DownloadQueue.cs:83`（事件注释）、`:661-663`（有意不裁剪）、`Core/Ffmpeg/FfmpegProgressParser.cs:155-157`

`TaskProgressChanged` 的注释写着「同一任务的进度按发生顺序触发且**单调不减**」，但：
- `DownloadQueue.OnProgressReported` 明确不裁剪（注释也说明了原因）；
- ffmpeg 的 `time=` / `out_time=` 在遇到流不连续时会**回退**，`FfmpegProgressParser` 也不做单调性约束。

测试 `Should_ReportMonotonicProgress` 用的是 `FakeDownloadHandler`（递增行为），**没有覆盖 ffmpeg 场景**，因此这个不成立的承诺被测试「验证」通过。

**建议**：把注释改为「进度可能因重试或流不连续而回退，订阅方应自行做 UI 层平滑」。

---

### M10 `DownloadTask.OutputBytes` 语义混乱，导致失败行显示随机体积

**位置**：`Core/Downloads/DownloadQueue.cs:654-663`、`App/Panes/DownloadPane.cs:296`

```csharp
private void OnProgressReported(DownloadTask task, DownloadProgress progress)
{
    if (task.OutputBytes == 0 && progress.DownloadedBytes > 0)
    {
        task.OutputBytes = progress.DownloadedBytes;   // ← 只在第一次生效，之后永不更新
    }
    task.LastProgressPercent = progress.Percent;
    // ...
}
```

`OutputBytes` 在成功时会被 `:567` 回填为真实体积，所以「已完成」行正常；但**失败/取消行的「大小」列会显示「首次进度采样的那个随机值」**（例如 `80 KB`），因为 `DisplayText.Size` 只对 `<= 0` 返回 `-`。

**建议**：要么删掉这个中途赋值（失败行统一显示 `-`），要么把字段语义明确为「最近一次进度字节数」并每次更新。当前这种「写一次就不管了」的形态既非前者也非后者。

---

### M11 「60 秒空闲超时」在两处独立实现，且探测阶段用的是**总时长**语义

**位置**：`Core/Downloads/HttpDownloadOptions.cs:69 / 84`、`Infrastructure/Downloads/HttpDownloadHandler.cs:199-204`

`ProbeTimeout`（默认 30 秒）通过 `timeout.CancelAfter` 施加，是**总时长**；`IdleTimeout`（默认 60 秒）在读流循环里逐次重置，是**空闲时长**。两者语义不同但在配置类里并列呈现，容易被误读为同一维度。建议在 `ProbeTimeout` 的文档注释里显式点明「这是总时长上限，而 `IdleTimeout` 是空闲时长」——目前 `IdleTimeout` 的注释解释得很清楚，`ProbeTimeout` 没有做这个对照。

---

## 4. P2 · 可延后

| 编号 | 位置 | 问题 |
| --- | --- | --- |
| L1 | `FfmpegLocator.cs:121` / `AppHost.cs:321` | `ClearCache()` 无调用方（死代码）。`AppHost` 每次重建管线都新建定位器，已达成同样效果 |
| L2 | `RequestContext.cs:64` | `ExtraHeaders` 的 `init` 访问器会让 `System.Text.Json` 反序列化时**替换**字典实例，丢掉 `OrdinalIgnoreCase` 比较器 → 往返一次后同名头可能重复 |
| L3 | `ProcessRunner.cs:85-91 / 111` | 依赖 `ErrorDataReceived` 回调；`WaitForExitAsync` 是否保证已排队的回调全部执行完毕**需要核实**。若不保证，`FfmpegDownloadHandler.BuildErrorDetail` 取的 stderr 尾部可能截断——而最后几行恰恰是真正的错误原因。建议加一个显式的 stderr 排空同步点（或改用 `process.StandardError.ReadToEndAsync()` 自行按行切分） |
| L4 | `HttpDownloadHandler.cs:210-226` | 探测用 `using var response` 后立即释放且不读正文 → 每次探测都会中断连接，无法复用。可优化，非缺陷 |
| L5 | `CookieExporter.cs:91-108` | 同名 Cookie（不同 domain/path）会拼出重复键。建议按 domain/path 排序并去重 |
| L6 | `DownloadQueue.cs:427-428` | `Dispose` 刻意不释放 `_slots`（理由充分），但泵任务未被 await，进程退出时可能仍在运行。当前无害，若将来支持「运行时重建」需重新评估 |
| L7 | `BrowserPane.cs:306-318` | `NormalizeInput` 的域名判定为「含点且不含空格」——`1.5` 这类输入会被当作域名拼成 `https://1.5`。概率低，体验瑕疵 |
| L8 | `AppHost.cs:186-199` | 输出文件名去重上限 999，超出后直接复用 `(999)` 名字并**覆盖**。极端情况，可接受 |
| L9 | 安全 | 会话 Cookie 通过 `-headers` 出现在 ffmpeg 的**命令行**上，同机其它进程可通过 WMI 读取命令行拿到它。这是 ffmpeg 没有「从文件读 header」选项导致的**无法完全规避的取舍**，但应在 README 的隐私说明里点明，而不是默认用户不会想到 |
| L10 | 文档一致性 | 头注释模板里的 `Copyright (c) 2023 RiverLand` 与 `创建时间：2023/12/6` 在本项目全部实现为 `2026`（已核实：66 个文件全部为 2026，且 66 个「唯一标识」GUID **无重复**）。若模板年份需要严格照抄，请告知，批量可改 |

---

## 5. 架构与工程建议

### A1 测试覆盖的空白恰好等于缺陷的分布

| 层 | 代码量（约） | 测试 |
| --- | --- | --- |
| Core | 1800 行 | 覆盖良好（100 个用例） |
| Infrastructure | 1300 行 | 覆盖尚可（下载器、ffmpeg 参数、解析器有测；嗅探/存储/进程无测） |
| **App（UI）** | **1500 行** | **0** |

P0 的 5 个问题里，H1、H4 归属 App 层，H2、H3、H5 归属层间接缝。这不是巧合。

**但我不建议「给 WinForms 补 UI 测试」**——性价比低。建议改为**把 UI 里的纯逻辑抽出来**，它们才是真正值得且容易测的：

- `AppHost.BuildOutputPath` / `SanitizeFileName` / `ResolveOutputDirectory` → 抽 `OutputPathResolver`（截断、非法字符、同名去重、目录回退四条分支目前全无测试）
- `DownloadPane` 的「状态 → 页签」映射与「行迁移」策略 → 抽成纯函数（H1 的修复若有测试守护，就不会复发）
- `BrowserPane.NormalizeInput` → 抽到 Core/`Formatting`

### A2 `DownloadPane` 的「三页签列结构不一致 + 行跨页签复用」是结构性张力

H1 不是实现疏忽，而是「复用行以保留进度」与「每页签列不同」两个目标天然冲突的必然结果。长期看有两条路：

1. **统一三个页签的列结构**（都 5 列，待下载页签多出的列留空）——推荐，代价小；
2. 不复用行，改为「迁移时把进度摘要一起带过去」——复杂度更高。

### A3 `AppHost` 职责过多（501 行，承担了 5 件事）

组合根 + 事件总线 + 持久化编排 + 输出路径策略 + 文件名清洗。其中后两者与「装配」无关。建议拆出 `OutputPathResolver`（见 A1）与 `TaskPersistenceCoordinator`（把 `SchedulePersist` / `PersistQuietlyAsync` / `SaveNow` / `_persistGate` 打包，顺带修掉 M3）。

### A4 `DownloadQueue` 795 行，是并发敏感度最高的类

建议把「重试决策 + 退避簿记」（`MarkFailed` + `_retryNotBefore`）抽成独立的 `RetryScheduler`，让真正需要小心审读的并发代码变少。H3 就出在 `PumpAsync`/`RunTaskAsync` 的临界区划分上——类越小，这种窗口越容易被看见。

### A5 文档与实现的一致性需要机制保障

本次审查发现的 P1 里有 **3 条（M8、M9、M2 的按钮提示）属于「注释/提示与实现相反」**。这类问题的危害被低估了：它们会让后续维护者基于错误前提做修改。建议：

- 对「承诺行为」的注释（单调性、落定语义、按钮行为），在对应测试里加一条断言，把注释变成可执行的约定；
- 或者干脆把这类承诺从注释里删掉，只描述事实。

---

## 6. 已核实**没有**问题的部分

为避免审查报告只呈现负面信息，以下是主动核查后确认健壮的部分：

| 项 | 结论 |
| --- | --- |
| 头注释完整性 | 66 个手写 `.cs` 文件全部通过（7 个必填字段齐全） |
| 「唯一标识」GUID | 66 个文件 66 个不同 GUID，**无重复**（脚本核实） |
| 断点续传的正确性 | 「每分片一个 `.partN` + 按长度判定完整性」的设计规避了「预分配整文件留下空洞」的经典陷阱；`existing > ExpectedLength` 时主动重下，方向安全 |
| 分片拼接顺序 | `segments.OrderBy(s => s.Index)` 显式排序，且测试用**位置相关**的字节序列断言逐字节相等（`CreatePatternContent`）——这是一个设计得很好的测试 |
| 事件顺序 | 自定义 `SynchronousProgress<T>` 替代框架 `Progress<T>`，正确规避了「进度事件晚于状态变更」的乱序问题，且让测试可确定性断言 |
| 锁纪律 | `DownloadQueue` 的取消动作一律放在锁外（`:263`、`:319` 的注释），正确规避了「锁内取消 → 回调反向申请锁」的死锁 |
| `SplitContainer` 初始化 | 「先解除最小尺寸 → 设位置 → 最后施加最小尺寸」的顺序正确，注释也解释了为什么（这是 WinForms 的经典坑，处理得比大多数代码干净） |
| `ProcessRunner` 的三处细节 | `ArgumentList` 逐参数传递、**stdout 必须抽干**、`Kill(entireProcessTree: true)` ——三条都命中真实故障模式，注释也说清了原因 |
| 持久化原子性 | `.tmp` + `MoveFile` 同卷改名，读取侧对 `JsonException/IOException/UnauthorizedAccessException` 全部容错返回默认值，方向正确 |
| `RebuildPipeline` 的任务迁移 | 保留 `Id`、把 `Running` 降级为 `Pending`、重新 `Start()`——语义正确，且 `AppHost` 作为稳定事件端点免除界面重复订阅，这个设计很好 |
| DRM 判定 | `SAMPLE-AES`/`SESSION-KEY` 明确识别为不支持，且 `DownloadResult.Fail` 里强制 `IsRetryable = false`（`:85`）——避免了「明知不可能成功还耗尽重试次数」 |
| 取消的语义分层 | 「用户取消清理半成品 vs 网络错误保留分片以续传」的区分依据正确（看 `cancellationToken` 是否真被触发），测试也覆盖了 |

---

## 7. 建议的修复顺序

| 顺序 | 项 | 理由 |
| --- | --- | --- |
| 1 | **H1** | 修复成本极低（约 10 行），用户可感知度最高，且会立刻让已有的大量中文错误提示变得可见 |
| 2 | **H5-a / H5-c** | 「取消后留下损坏 mp4」是会误导用户的文件残留；改成写 `.part` 再改名即可根治 |
| 3 | **H2** | 把「空闲超时」从「永久失败」改为「可重试」，并让分片级重试真正生效 |
| 4 | **H3 / H4** | 需要更仔细地重划 `DownloadQueue` 的临界区，建议连同 A4 的抽取一起做 |
| 5 | **M1 / M2** | 决定嗅探能力的实际可用上限，建议一起改（都涉及指纹集合的生命周期） |
| 6 | M3 / M4 / M6 / M8 | 低成本高收益：加锁、加 await-catch、加 CRLF 过滤、改注释 |
| 7 | M5 / M7 / M9-M11 + P2 | 可随下一轮迭代处理 |

**建议给 H1 / H2 / H3 / H4 各补一条回归测试**——H1 需要把「行迁移后的子项数」抽成可测的纯函数（见 A1），其余三条在 Core 层即可用 `FakeDownloadHandler` 构造（H3/H4 需要能确定性地制造并发窗口，可用「在 handler 内用 `ManualResetEventSlim` 阻塞并等待测试信号」的手法）。

---

## 附：本次审查中使用的核实手段

- 逐文件阅读全部 66 个源文件 + 8 个测试文件原文
- grep 交叉验证「疑似死代码」的调用方（`ClearCache`、`WebView2Sniffer.Clear`、`MaxTrackedItems`）
- 脚本校验头注释 7 个字段完整性、GUID 唯一性、版权年份分布
- 对 H1 逐行推演「行创建 → 迁移 → 填充」三步的子项计数变化
- 对 H2/H3/H4 逐行推演并发时序，并核对既有测试是否覆盖该路径（均未覆盖）
