/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： JsHookScript
*版本号： V1.0.0.0
*唯一标识：f8594267-b670-4b72-87ae-55cdeca6893d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:38:00
*描述：注入到页面的嗅探脚本源码，通过劫持网络 API 捕获动态拼接的视频地址。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:38:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 注入脚本源码。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要 JS 注入</b>：仅靠响应监听会漏掉两类地址 ——
/// 一是页面用 JS 动态拼接、从未真正发过请求的地址；
/// 二是播放器先请求再用 <c>blob:</c> 转交 MSE 的场景。
/// 劫持网络 API 可以在地址「被构造出来的那一刻」就拿到它。
/// </para>
/// <para>
/// <b>脚本约束</b>：
/// <list type="bullet">
///   <item><description>使用 ES5 语法（<c>var</c>、无箭头函数、无模板字符串），以兼容站点可能存在的旧内核降级路径。</description></item>
///   <item><description>全程 <c>try/catch</c> 包裹，任何异常都不得影响宿主页面的正常播放。</description></item>
///   <item><description>通过 <c>__bvgbHooked</c> 标志保证幂等，避免因重复注入导致请求被上报多次。</description></item>
///   <item><description>只上报 <c>http(s)</c> 开头的地址：<c>blob:</c> 与 <c>data:</c> 无法直接下载，上报只会污染列表。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>未劫持 MSE 的原因</b>：<c>MediaSource.addSourceBuffer</c> 的入参是 MIME 类型而非地址，
/// 劫持它拿不到任何可下载信息；而它真正的价值在于「分段喂数据」，
/// 那些数据无法还原成完整文件。因此本工具对纯 MSE 站点只能尽力而为。
/// </para>
/// </remarks>
public static class JsHookScript
{
    /// <summary>
    /// 注入到每个新文档的嗅探脚本源码。
    /// </summary>
    public const string Source = """
        (function () {
          if (window.__bvgbHooked === true) { return; }
          window.__bvgbHooked = true;

          var EXTENSION_PATTERN = /\.(m3u8|m3u|ts|m4s|mp4|mpd)(\?|#|$)/i;
          var PLAYLIST_HINT_PATTERN = /m3u8|mpegurl|dash\+xml/i;
          // 显式排除图片/音频后缀：某些 CDN 会把封面图以 video/* 的 Content-Type 返回，
          // 这里在 JS 层先挡一层，避免给后端增加无意义的过滤开销
          var BLACKLIST_PATTERN = /\.(jpg|jpeg|png|gif|webp|bmp|heic|avif|svg|mp3|wav|flac|aac|ogg|m4a|opus|css|js|html?|woff2?|ttf|eot)(\?|#|$)/i;

          function report(value, source) {
            try {
              if (!value) { return; }

              var url = String(value);
              if (url.indexOf('http') !== 0) { return; }

              // 黑名单优先：明确不是视频的 URL 一律跳过，即便 Content-Type 可能误报
              if (BLACKLIST_PATTERN.test(url)) { return; }

              if (!EXTENSION_PATTERN.test(url) && !PLAYLIST_HINT_PATTERN.test(url)) { return; }

              if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                window.chrome.webview.postMessage({
                  type: 'bvgb-sniff',
                  url: url,
                  source: source || 'jshook'
                });
              }
            } catch (e) { }
          }

          try {
            var originalOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function (method, url) {
              report(url, 'jshook-xhr');
              return originalOpen.apply(this, arguments);
            };
          } catch (e) { }

          try {
            if (window.fetch) {
              var originalFetch = window.fetch;
              window.fetch = function (input) {
                try {
                  if (typeof input === 'string') {
                    report(input, 'jshook-fetch');
                  } else if (input && input.url) {
                    report(input.url, 'jshook-fetch');
                  }
                } catch (e) { }
                return originalFetch.apply(this, arguments);
              };
            }
          } catch (e) { }

          function patchMediaElement(prototype) {
            if (!prototype) { return; }

            try {
              var descriptor = Object.getOwnPropertyDescriptor(prototype, 'src');
              if (descriptor && descriptor.set) {
                Object.defineProperty(prototype, 'src', {
                  configurable: true,
                  enumerable: descriptor.enumerable,
                  get: descriptor.get,
                  set: function (value) {
                    report(value, 'jshook-media');
                    return descriptor.set.call(this, value);
                  }
                });
              }
            } catch (e) { }

            try {
              var originalLoad = prototype.load;
              if (typeof originalLoad === 'function') {
                prototype.load = function () {
                  try { report(this.getAttribute('src'), 'jshook-media'); } catch (e) { }
                  return originalLoad.apply(this, arguments);
                };
              }
            } catch (e) { }
          }

          patchMediaElement(window.HTMLMediaElement && window.HTMLMediaElement.prototype);
          patchMediaElement(window.HTMLSourceElement && window.HTMLSourceElement.prototype);
        })();
        """;

    /// <summary>
    /// 主动扫描<b>当前文档</b>媒体元素地址的脚本源码（右键「下载视频」触发）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Source"/> 是「文档创建时」注入的钩子，对已经加载完的页面不再生效；
    /// 本脚本用于兜底：立即扫描 <c>&lt;video&gt; / &lt;audio&gt;</c> 及其
    /// <c>&lt;source&gt;</c> 子元素当前的地址并上报，且 1.2 秒后再扫一遍，
    /// 以覆盖「播放器在页面就绪后才动态绑定地址」的站点。
    /// </para>
    /// <para>
    /// 脚本返回一个 Promise：<c>ExecuteScriptAsync</c> 会等待它兑现，
    /// 保证两次扫描都完成后宿主侧才收到「扫描结束」的信号。
    /// </para>
    /// </remarks>
    public const string PageScanSource = """
        (function () {
          function post(url) {
            try {
              if (!url) { return; }
              url = String(url);
              if (url.indexOf('http') !== 0) { return; }
              if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                window.chrome.webview.postMessage({
                  type: 'bvgb-sniff',
                  url: url,
                  source: 'scan'
                });
              }
            } catch (e) { }
          }

          function pass() {
            try {
              var media = document.querySelectorAll('video, audio');
              for (var i = 0; i < media.length; i++) {
                var el = media[i];
                post(el.currentSrc || el.src);
                post(el.getAttribute && el.getAttribute('src'));
                var sources = el.querySelectorAll('source');
                for (var j = 0; j < sources.length; j++) {
                  post(sources[j].src);
                }
              }
            } catch (e) { }
          }

          return new Promise(function (resolve) {
            pass();
            setTimeout(function () {
              pass();
              resolve(true);
            }, 1200);
          });
        })();
        """;
}
