using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 网页面板的音频后端：网页发指令，Unity 这一侧真正出声。
	/// </summary>
	/// <remarks>
	/// 为什么不在页面里用 <c>&lt;audio&gt;</c>：面板是 <c>LoadHtml</c> 直接塞进来的 data: 页面，
	/// 没有 baseUrl、没有本地文件访问权，联网也受 CEF 的跨域限制；而 Unity 侧
	/// <see cref="UnityWebRequestMultimedia"/> + <see cref="AudioSource"/> 既能拉在线 mp3，
	/// 也能直接播 Mod 目录里的本地音频（离线可用），进度/时长/音量都由引擎给出，不需要页面自己掐表。
	///
	/// 协议（页面 → 宿主，走 <c>window.vuplex.postMessage(JSON)</c>）：
	/// <code>
	/// {"type":"audio","op":"play","url":"https://...","title":"歌名"}
	/// {"type":"audio","op":"playLocal","path":"C:/.../a.mp3","title":"歌名"}
	/// {"type":"audio","op":"listLocal"}
	/// {"type":"audio","op":"pause"|"resume"|"stop"}
	/// {"type":"audio","op":"seek","position":12.5}
	/// {"type":"audio","op":"volume","value":0.8}
	/// </code>
	/// 宿主 → 页面：<c>window.__bpsimHost({type:"audio",op:"state"|"localList",...})</c>。
	/// </remarks>
	public sealed class ModPanelAudio : MonoBehaviour
	{
		private static readonly string[] kAudioExtensions = { ".mp3", ".ogg", ".wav", ".m4a", ".aac", ".flac" };

		private AudioSource m_source;
		private Coroutine m_loadRoutine;
		private UnityWebRequest m_request;
		private string m_title = string.Empty;
		private string m_error = string.Empty;
		private bool m_loading;
		private float m_nextReport;
		private string m_modDir = string.Empty;
		private string m_modId = string.Empty;
		private bool m_bound;
		private bool m_reportedStopped;

		/// <summary>把一条消息发回页面（JSON 文本）。</summary>
		public Action<string> Post;

		public bool IsPlaying => m_source != null && m_source.isPlaying;

		public string Describe()
		{
			return "audio title='" + m_title + "' playing=" + IsPlaying + " loading=" + m_loading
				+ " error=" + (m_error.Length == 0 ? "-" : m_error);
		}

		/// <summary>绑定 Mod 目录（本地曲库扫描用）并开始接收指令。</summary>
		public void Bind(string modId, string modDir)
		{
			m_modId = modId ?? string.Empty;
			m_modDir = modDir ?? string.Empty;
			if (m_bound)
			{
				return;
			}
			m_bound = true;
			EnsureSource();
		}

		private void EnsureSource()
		{
			if (m_source != null)
			{
				return;
			}
			m_source = gameObject.GetComponent<AudioSource>();
			if (m_source == null)
			{
				m_source = gameObject.AddComponent<AudioSource>();
			}
			m_source.playOnAwake = false;
			m_source.loop = false;
			m_source.spatialBlend = 0f; // 2D，不受相机位置影响
			m_source.volume = 0.85f;
			m_source.ignoreListenerPause = true; // 游戏暂停时音乐继续
			m_source.ignoreListenerVolume = false;
		}

		private void Update()
		{
			if (m_source == null || m_source.clip == null)
			{
				return;
			}
			// 只在播放时持续上报（进度条要跟着走）；停下来之后补报一次，避免每 0.25 秒空转。
			if (!m_source.isPlaying)
			{
				if (m_reportedStopped)
				{
					return;
				}
				m_reportedStopped = true;
			}
			else
			{
				m_reportedStopped = false;
			}
			if (Time.unscaledTime < m_nextReport)
			{
				return;
			}
			m_nextReport = Time.unscaledTime + 0.25f;
			ReportState();
		}

		/// <summary>处理页面指令（已经解析好的 JSON 对象）。</summary>
		public void Handle(JObject msg)
		{
			EnsureSource();
			string op = (string)msg["op"] ?? string.Empty;
			switch (op.ToLowerInvariant())
			{
				case "play":
					PlayRemote((string)msg["url"], (string)msg["title"]);
					break;
				case "playlocal":
					PlayLocal((string)msg["path"], (string)msg["title"]);
					break;
				case "listlocal":
					PostLocalList();
					break;
				case "pause":
					if (m_source != null && m_source.clip != null)
					{
						m_source.Pause();
					}
					ReportState();
					break;
				case "resume":
					if (m_source != null && m_source.clip != null)
					{
						m_source.UnPause();
					}
					ReportState();
					break;
				case "stop":
					StopPlayback();
					ReportState();
					break;
				case "seek":
					if (m_source != null && m_source.clip != null && msg["position"] != null)
					{
						m_source.time = Mathf.Clamp((float)msg["position"], 0f, m_source.clip.length);
					}
					ReportState();
					break;
				case "volume":
					if (m_source != null && msg["value"] != null)
					{
						m_source.volume = Mathf.Clamp01((float)msg["value"]);
					}
					break;
				default:
					Debug.LogWarning("[ModAudio] 未知指令: " + op);
					break;
			}
		}

		private void PlayRemote(string url, string title)
		{
			if (string.IsNullOrEmpty(url))
			{
				Fail("没有可播放的地址");
				return;
			}
			if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
			{
				Fail("只支持 http(s) 地址: " + url);
				return;
			}
			AudioType type = GuessAudioType(url);
			StartRequest(UnityWebRequestMultimedia.GetAudioClip(url, type), title);
		}

		private void PlayLocal(string path, string title)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				Fail("本地文件不存在: " + path);
				return;
			}
			string uri;
			try
			{
				uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
			}
			catch (Exception ex)
			{
				Fail("路径无效: " + ex.Message);
				return;
			}
			if (string.IsNullOrEmpty(title))
			{
				title = Path.GetFileNameWithoutExtension(path);
			}
			StartRequest(UnityWebRequestMultimedia.GetAudioClip(uri, GuessAudioType(path)), title);
		}

		private void StartRequest(UnityWebRequest request, string title)
		{
			StopPlayback();
			m_title = title ?? string.Empty;
			m_error = string.Empty;
			m_request = request;
			m_loadRoutine = StartCoroutine(WaitForRequest(request));
			ReportState();
		}

		private IEnumerator WaitForRequest(UnityWebRequest request)
		{
			m_loading = true;
			if (request.url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
			{
				// 网易云 CDN 对 UA/Referer 挑剔，照浏览器的样子发。
				try
				{
					request.SetRequestHeader("User-Agent",
						"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
					request.SetRequestHeader("Referer", "https://music.163.com/");
				}
				catch (Exception) { }
			}
			request.timeout = 30;
			yield return request.SendWebRequest();
			m_loading = false;
			if (request.result != UnityWebRequest.Result.Success)
			{
				Fail("加载失败: " + request.error);
			}
			else
			{
				AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
				if (clip == null)
				{
					Fail("解码失败（格式不支持？）");
				}
				else
				{
					clip.name = string.IsNullOrEmpty(m_title) ? "mod-audio" : m_title;
					m_source.clip = clip;
					m_source.time = 0f;
					m_source.Play();
					m_error = string.Empty;
				}
			}
			ReportState();
			request.Dispose();
			if (m_request == request)
			{
				m_request = null;
				m_loadRoutine = null;
			}
		}

		private void Fail(string message)
		{
			m_error = message ?? "error";
			m_loading = false;
			Debug.LogWarning("[ModAudio] " + m_error);
			ReportState();
		}

		private void StopPlayback()
		{
			if (m_loadRoutine != null)
			{
				StopCoroutine(m_loadRoutine);
				m_loadRoutine = null;
			}
			if (m_request != null)
			{
				m_request.Abort();
				m_request.Dispose();
				m_request = null;
			}
			m_loading = false;
			if (m_source != null)
			{
				m_source.Stop();
				if (m_source.clip != null)
				{
					m_source.clip = null;
				}
			}
		}

		private void ReportState()
		{
			if (Post == null)
			{
				return;
			}
			EnsureSource();
			JObject obj = new JObject
			{
				["type"] = "audio",
				["op"] = "state",
				["playing"] = m_source.isPlaying,
				["loading"] = m_loading,
				["position"] = m_source.clip != null ? m_source.time : 0f,
				["duration"] = m_source.clip != null ? m_source.clip.length : 0f,
				["title"] = m_title,
				["error"] = m_error,
				["volume"] = m_source.volume
			};
			Post(obj.ToString(Newtonsoft.Json.Formatting.None));
		}

		private void PostLocalList()
		{
			JArray items = new JArray();
			JArray dirs = new JArray();
			foreach (string dir in EnumerateLocalDirs())
			{
				dirs.Add(dir);
				if (!Directory.Exists(dir))
				{
					continue;
				}
				string[] files;
				try
				{
					files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
				}
				catch (Exception)
				{
					continue;
				}
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				foreach (string file in files)
				{
					if (!IsAudioFile(file))
					{
						continue;
					}
					items.Add(new JObject
					{
						["name"] = Path.GetFileNameWithoutExtension(file),
						["path"] = file.Replace('\\', '/')
					});
				}
			}
			JObject obj = new JObject
			{
				["type"] = "audio",
				["op"] = "localList",
				["items"] = items,
				["dirs"] = dirs
			};
			Post?.Invoke(obj.ToString(Newtonsoft.Json.Formatting.None));
		}

		private IEnumerable<string> EnumerateLocalDirs()
		{
			if (!string.IsNullOrEmpty(m_modDir))
			{
				yield return Path.Combine(m_modDir, "music");
			}
			if (!string.IsNullOrEmpty(m_modId))
			{
				yield return Path.Combine(ModPaths.Root, m_modId, "music");
			}
			// 全局音乐目录：Mods/Music，所有面板共用
			yield return Path.Combine(ModPaths.Root, "Music");
		}

		private static bool IsAudioFile(string path)
		{
			string ext = Path.GetExtension(path);
			if (string.IsNullOrEmpty(ext))
			{
				return false;
			}
			ext = ext.ToLowerInvariant();
			for (int i = 0; i < kAudioExtensions.Length; i++)
			{
				if (kAudioExtensions[i] == ext)
				{
					return true;
				}
			}
			return false;
		}

		private static AudioType GuessAudioType(string pathOrUrl)
		{
			string lower = (pathOrUrl ?? string.Empty).ToLowerInvariant();
			int query = lower.IndexOf('?');
			if (query >= 0)
			{
				lower = lower.Substring(0, query);
			}
			if (lower.EndsWith(".ogg", StringComparison.Ordinal)) return AudioType.OGGVORBIS;
			if (lower.EndsWith(".wav", StringComparison.Ordinal)) return AudioType.WAV;
			if (lower.EndsWith(".m4a", StringComparison.Ordinal)) return AudioType.MPEG;
			return AudioType.MPEG;
		}

		private void OnDestroy()
		{
			StopPlayback();
		}
	}
}
