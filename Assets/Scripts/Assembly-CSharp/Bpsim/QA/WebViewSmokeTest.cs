using System.Collections;
using System.IO;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.UI;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 移动端网页后端的真机冒烟测试。
	///
	/// 只在设备上存在开关文件时运行：
	///   adb shell "mkdir -p /data/data/&lt;包名&gt;/files &amp;&amp; touch /data/data/&lt;包名&gt;/files/webview_smoke.on"
	/// 运行结果同时写 logcat（tag 形如 [WebViewSmoke]）和 &lt;persistentDataPath&gt;/webview_smoke.log，
	/// 验证的是：UniWebView 创建 → LoadHTMLString → 页面加载完成 → 页面→宿主消息（shim）→ 宿主→页面消息。
	/// </summary>
	public sealed class WebViewSmokeTest : MonoBehaviour
	{
		private const string FlagFile = "webview_smoke.on";
		private const string LogFile = "webview_smoke.log";

		private static WebViewSmokeTest s_instance;

		private HtmlPanelView m_view;
		private Canvas m_canvas;
		private int m_step;
		private string m_report = string.Empty;
		private bool m_finished;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			string flag = Path.Combine(Application.persistentDataPath, FlagFile);
			if (!File.Exists(flag))
			{
				return;
			}
			if (s_instance != null)
			{
				return;
			}
			GameObject go = new GameObject("WebViewSmokeTest");
			DontDestroyOnLoad(go);
			s_instance = go.AddComponent<WebViewSmokeTest>();
		}

		private IEnumerator Start()
		{
			yield return new WaitForSeconds(2.5f);

			m_canvas = ModUiKit.CreateCanvas("WebViewSmokeCanvas", 32000);
			RectTransform host = ModUiKit.Panel(m_canvas.transform, new Color(0.09f, 0.10f, 0.13f, 1f), 8);
			host.name = "SmokeHost";
			ModUiKit.SetRect(host, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f), Vector2.zero, Vector2.zero);

			m_view = HtmlPanelView.Attach(m_canvas.gameObject, host, false, new Color(0.09f, 0.10f, 0.13f, 1f));
			if (m_view == null)
			{
				Finish("Attach 返回 null");
				yield break;
			}

			m_view.MessageReceived += OnMessage;
			m_view.PageLoaded += OnPageLoaded;
			m_view.Failed += OnFailed;
			m_view.Ready += OnReady;

			Log("等待后端就绪，Backend=" + m_view.BackendName);
			yield return new WaitForSeconds(75f);
			Finish("超时收尾");
		}

		private static void Log(string message)
		{
			Debug.Log("[WebViewSmoke] " + message);
		}

		private void OnReady()
		{
			Log("后端就绪: " + m_view.Status);
			m_view.LoadHtml(BuildPage(), "file:///");
		}

		private void OnPageLoaded(string url)
		{
			Log("页面加载完成: " + url);
			m_step++;
			StartCoroutine(AfterPageLoaded());
		}

		private IEnumerator AfterPageLoaded()
		{
			yield return new WaitForSeconds(1f);
			Log("执行宿主→页面：window.__bpsimHost({type:'ping'})");
			m_view.ExecuteJavaScript("(window.__bpsimHost||function(){})(" + "{\"type\":\"ping\",\"n\":1}" + ");");
			yield return new WaitForSeconds(2f);
			Log("执行页面→宿主：window.vuplex.postMessage");
			m_view.ExecuteJavaScript("window.vuplex && window.vuplex.postMessage(JSON.stringify({type:'direct',n:2}));");
			yield return new WaitForSeconds(2f);
			yield return StartCoroutine(PhaseRealPanel());
		}

		/// <summary>第二阶段：走真实链路 ModPanelService.Open（合成页换成真 Mod 的 panel.html）。</summary>
		private IEnumerator PhaseRealPanel()
		{
			if (m_canvas != null)
			{
				Destroy(m_canvas.gameObject);
				m_canvas = null;
				yield return new WaitForSeconds(1f);
			}

			string dir = Path.Combine(Application.persistentDataPath, "Mods", "netease-music");
			Log("加载真实 Mod 包: " + dir);
			Bpsim.Mods.ModPackage pkg = null;
			try
			{
				pkg = ModPackageStore.TryLoadPackage(dir, ModSource.Installed);
			}
			catch (System.Exception ex)
			{
				Log("包加载异常: " + ex.Message);
			}

			if (pkg == null || pkg.Manifest == null || pkg.Manifest.panels == null || pkg.Manifest.panels.Count == 0)
			{
				Finish("真实面板跳过：包或面板缺失");
				yield break;
			}

			ModPanelSpec spec = pkg.Manifest.panels[0];
			Log("打开面板 " + spec.id + " html=" + spec.html);
			ModPanelService.Open(pkg, spec);
			yield return new WaitForSeconds(10f);
			Log("OpenCount=" + ModPanelService.OpenCount + " isOpen=" + ModPanelService.IsOpen(pkg.Id + ":" + spec.id));
			Finish("真实面板阶段结束");
		}

		private void OnMessage(string text)
		{
			Log("收到页面消息: " + text);
			m_report += "msg=" + text + "\n";
		}

		private void OnFailed(string reason)
		{
			Log("后端失败: " + reason);
			m_report += "failed=" + reason + "\n";
			Finish("失败");
		}

		private void Finish(string reason)
		{
			if (m_finished)
			{
				return;
			}
			m_finished = true;
			string status = m_view != null ? m_view.Status : "no-view";
			string body = "reason=" + reason + "\nstatus=" + status + "\n" + m_report;
			Log("结束: " + body.Replace("\n", " | "));
			try
			{
				File.WriteAllText(Path.Combine(Application.persistentDataPath, LogFile), body);
			}
			catch (System.Exception ex)
			{
				Log("写日志失败: " + ex.Message);
			}
			StopAllCoroutines();
		}

		private static string BuildPage()
		{
			return "<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
				+ "<style>body{margin:0;background:#15181f;color:#e8ecf3;font-family:sans-serif;padding:16px}"
				+ "button{font-size:18px;padding:10px 16px;margin:6px 0;display:block;width:100%}</style></head><body>"
				+ "<h2>WebView smoke</h2><div id='out'>就绪</div>"
				+ "<button onclick=\"window.vuplex.postMessage(JSON.stringify({type:'tap',t:Date.now()}))\">postMessage</button>"
				+ "<script>window.__bpsimHost=function(m){var o=document.getElementById('out');o.textContent=JSON.stringify(m);"
				+ "window.vuplex.postMessage(JSON.stringify({type:'echo',m:m}));};"
				+ "window.vuplex && window.vuplex.postMessage(JSON.stringify({type:'boot'}));</script></body></html>";
		}
	}
}
