using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Bpsim.Modding
{
	public sealed class ModWebRequest
	{
		public string Method;
		public string Path;
		public byte[] Body;
		public string RequestId;
		public string Text => Encoding.UTF8.GetString(Body ?? Array.Empty<byte>());
	}

	public sealed class ModWebResponse
	{
		public int Status = 200;
		public string ContentType = "application/json; charset=utf-8";
		public byte[] Body = Array.Empty<byte>();

		public static ModWebResponse Json(string json, int status = 200)
		{
			return new ModWebResponse { Status = status, Body = Encoding.UTF8.GetBytes(json ?? "{}") };
		}
	}

	/// <summary>
	/// Authenticated loopback hosting for an enabled Mod. No listener exists until Start is called.
	/// Network access stays in the host, rather than weakening the Mod assembly scanner.
	/// </summary>
	public sealed class ModWebService : IDisposable
	{
		private sealed class Pending
		{
			public string Fingerprint;
			public readonly TaskCompletionSource<ModWebResponse> Result =
				new TaskCompletionSource<ModWebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
			public DateTime Deadline;
		}

		private readonly string m_root;
		private readonly string m_token;
		private readonly Func<ModWebRequest, ModWebResponse> m_handler;
		private readonly TcpListener m_listener;
		private readonly CancellationTokenSource m_cancel = new CancellationTokenSource();
		private readonly ConcurrentQueue<Action> m_mainThread = new ConcurrentQueue<Action>();
		private readonly Dictionary<string, Pending> m_requests = new Dictionary<string, Pending>(StringComparer.Ordinal);
		private readonly SemaphoreSlim m_connections = new SemaphoreSlim(12);
		private readonly GameObject m_runner;
		private int m_disposed;
		private const int MaximumBody = 24 * 1024 * 1024;

		public string Origin { get; }
		public string PageUrl => Origin + "/index.html#bpsim-token=" + m_token;
		public bool IsRunning => Volatile.Read(ref m_disposed) == 0;

		public static ModWebService Start(ModEntryPoint owner, string relativeDirectory, Func<ModWebRequest, ModWebResponse> handler)
		{
			ModHostResources.CheckOwner(owner);
			if (handler == null) throw new ArgumentNullException(nameof(handler));
			string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(owner.ModDirectory, relativeDirectory ?? string.Empty));
			string prefix = System.IO.Path.GetFullPath(owner.ModDirectory).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
			if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
				throw new ArgumentException("Web assets must exist inside this Mod's directory.");
			return ModHostResources.Own(owner, new ModWebService(owner.Id, directory, handler));
		}

		private ModWebService(Guid owner, string root, Func<ModWebRequest, ModWebResponse> handler)
		{
			m_root = root;
			m_handler = handler;
			byte[] token = new byte[32];
			using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(token);
			m_token = Convert.ToBase64String(token).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			m_listener = new TcpListener(IPAddress.Loopback, 0);
			m_listener.Start(32);
			Origin = "http://127.0.0.1:" + ((IPEndPoint)m_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
			m_runner = new GameObject("ModWebService_" + owner.ToString("N"));
			UnityEngine.Object.DontDestroyOnLoad(m_runner);
			m_runner.AddComponent<ModWebDispatcher>().Service = this;
			_ = AcceptClients();
		}

		private async Task AcceptClients()
		{
			try
			{
				while (IsRunning)
				{
					TcpClient client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
					_ = Serve(client);
				}
			}
			catch (Exception ex)
			{
				if (IsRunning) Debug.LogWarning("[ModWeb] Listener stopped: " + ex.Message);
			}
		}

		private async Task Serve(TcpClient client)
		{
			bool admitted = false;
			try
			{
				await m_connections.WaitAsync(m_cancel.Token).ConfigureAwait(false);
				admitted = true;
				using (client)
				using (NetworkStream stream = client.GetStream())
				{
					client.ReceiveTimeout = 15000;
					client.SendTimeout = 30000;
					byte[] header = await ReadHeader(stream).ConfigureAwait(false);
					string[] lines = Encoding.ASCII.GetString(header).Split(new[] { "\r\n" }, StringSplitOptions.None);
					string[] requestLine = lines[0].Split(' ');
					if (requestLine.Length != 3 || !requestLine[1].StartsWith("/", StringComparison.Ordinal))
						throw new IOException("Invalid HTTP request.");
					string method = requestLine[0];
					string target = requestLine[1].Split('?')[0];
					Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					for (int i = 1; i < lines.Length; i++)
					{
						int separator = lines[i].IndexOf(':');
						if (separator > 0 && !headers.TryAdd(lines[i].Substring(0, separator).Trim(), lines[i].Substring(separator + 1).Trim()))
							throw new IOException("Duplicate request header.");
					}
					headers.TryGetValue("Host", out string host);
					headers.TryGetValue("Origin", out string origin);
					if (host != new Uri(Origin).Authority || (!string.IsNullOrEmpty(origin) && origin != Origin))
					{
						await Write(stream, ModWebResponse.Json("{\"error\":\"Invalid local origin\"}", 403)).ConfigureAwait(false);
						return;
					}
					if (target.StartsWith("/api/", StringComparison.Ordinal))
					{
						headers.TryGetValue("Authorization", out string authorization);
						if (authorization != "Bearer " + m_token)
						{
							await Write(stream, ModWebResponse.Json("{\"error\":\"Mod session token required\"}", 401)).ConfigureAwait(false);
							return;
						}
						if (headers.ContainsKey("Transfer-Encoding"))
						{
							await Write(stream, ModWebResponse.Json("{\"error\":\"Chunked requests are not supported\"}", 400)).ConfigureAwait(false);
							return;
						}
						int length = 0;
						if (headers.TryGetValue("Content-Length", out string textLength) &&
							(!int.TryParse(textLength, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0 || length > MaximumBody))
						{
							await Write(stream, ModWebResponse.Json("{\"error\":\"Request body limit exceeded\"}", 413)).ConfigureAwait(false);
							return;
						}
						if (method != "GET" && method != "POST")
						{
							await Write(stream, ModWebResponse.Json("{\"error\":\"Method not allowed\"}", 405)).ConfigureAwait(false);
							return;
						}
						byte[] body = new byte[length];
						await ReadExact(stream, body).ConfigureAwait(false);
						headers.TryGetValue("X-Request-Id", out string requestId);
						if (method == "POST" && !Guid.TryParse(requestId, out _))
						{
							await Write(stream, ModWebResponse.Json("{\"error\":\"A unique X-Request-Id is required\"}", 400)).ConfigureAwait(false);
							return;
						}
						ModWebRequest request = new ModWebRequest { Method = method, Path = target, Body = body, RequestId = requestId };
						ModWebResponse response = await Dispatch(request).ConfigureAwait(false);
						await Write(stream, response).ConfigureAwait(false);
					}
					else if (method == "GET" || method == "HEAD")
					{
						await WriteFile(stream, target, method == "HEAD").ConfigureAwait(false);
					}
					else await Write(stream, ModWebResponse.Json("{\"error\":\"Method not allowed\"}", 405)).ConfigureAwait(false);
				}
			}
			catch (Exception)
			{
				// The peer may close a tab during a domain reload or while a large asset is being read.
				client.Dispose();
			}
			finally { if (admitted) m_connections.Release(); }
		}

		private async Task<ModWebResponse> Dispatch(ModWebRequest request)
		{
			Pending pending;
			string fingerprint = request.Method + " " + request.Path;
			using (SHA256 sha = SHA256.Create()) fingerprint += ":" + Convert.ToBase64String(sha.ComputeHash(request.Body));
			bool enqueue = true;
			lock (m_requests)
			{
				if (request.Method == "POST" && m_requests.TryGetValue(request.RequestId, out pending))
				{
					if (pending.Fingerprint != fingerprint) return ModWebResponse.Json("{\"error\":\"Request ID already used for different content\"}", 409);
					enqueue = false;
				}
				else
				{
					pending = new Pending { Fingerprint = fingerprint, Deadline = DateTime.UtcNow.AddSeconds(90) };
					if (request.Method == "POST")
					{
						if (m_requests.Count >= 128)
						{
							List<string> expired = new List<string>();
							foreach (KeyValuePair<string, Pending> item in m_requests)
								if (item.Value.Result.Task.IsCompleted) expired.Add(item.Key);
							foreach (string key in expired) m_requests.Remove(key);
							if (m_requests.Count >= 128) return ModWebResponse.Json("{\"error\":\"Too many pending requests\"}", 429);
						}
						m_requests.Add(request.RequestId, pending);
					}
				}
			}
			if (enqueue)
			{
				Pending captured = pending;
				m_mainThread.Enqueue(() =>
				{
					if (!IsRunning || DateTime.UtcNow > captured.Deadline)
					{
						captured.Result.TrySetResult(ModWebResponse.Json("{\"error\":\"Mod disabled or request expired before execution\"}", 503));
						return;
					}
					try { captured.Result.TrySetResult(m_handler(request) ?? ModWebResponse.Json("{}")); }
					catch (Exception ex)
					{
						Debug.LogWarning("[ModWeb] " + request.Path + ": " + ex.Message);
						captured.Result.TrySetResult(ModWebResponse.Json("{\"error\":\"Mod request failed; check the game log\"}", 500));
					}
				});
			}
			Task completed = await Task.WhenAny(pending.Result.Task, Task.Delay(95000, m_cancel.Token)).ConfigureAwait(false);
			return completed == pending.Result.Task ? await pending.Result.Task.ConfigureAwait(false) :
				ModWebResponse.Json("{\"error\":\"Response not confirmed. Do not automatically retry the write.\"}", 504);
		}

		internal void Pump()
		{
			for (int i = 0; i < 8 && m_mainThread.TryDequeue(out Action action); i++) action();
		}

		private async Task<byte[]> ReadHeader(NetworkStream stream)
		{
			using MemoryStream header = new MemoryStream();
			byte[] one = new byte[1];
			int terminator = 0;
			byte[] ending = { 13, 10, 13, 10 };
			while (header.Length < 16384)
			{
				int count = await stream.ReadAsync(one, 0, 1, m_cancel.Token).ConfigureAwait(false);
				if (count != 1) throw new EndOfStreamException();
				header.WriteByte(one[0]);
				terminator = one[0] == ending[terminator] ? terminator + 1 : (one[0] == 13 ? 1 : 0);
				if (terminator == 4) return header.ToArray();
			}
			throw new IOException("HTTP header too large.");
		}

		private async Task ReadExact(NetworkStream stream, byte[] body)
		{
			int offset = 0;
			while (offset < body.Length)
			{
				int count = await stream.ReadAsync(body, offset, body.Length - offset, m_cancel.Token).ConfigureAwait(false);
				if (count == 0) throw new EndOfStreamException();
				offset += count;
			}
		}

		private async Task WriteFile(NetworkStream stream, string target, bool head)
		{
			string relative = Uri.UnescapeDataString(target).TrimStart('/');
			if (relative.Length == 0) relative = "index.html";
			string[] segments = relative.Split('/');
			foreach (string segment in segments)
			{
				if (string.IsNullOrEmpty(segment) || segment == "." || segment == ".." ||
					segment.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0)
				{
					await Write(stream, ModWebResponse.Json("{\"error\":\"Invalid asset path\"}", 403)).ConfigureAwait(false);
					return;
				}
			}
			string path = m_root;
			foreach (string segment in segments)
			{
				path = System.IO.Path.Combine(path, segment);
				if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
				{
					await Write(stream, ModWebResponse.Json("{\"error\":\"Asset links are not followed\"}", 403)).ConfigureAwait(false);
					return;
				}
			}
			if (!File.Exists(path))
			{
				await Write(stream, ModWebResponse.Json("{\"error\":\"Asset not found\"}", 404)).ConfigureAwait(false);
				return;
			}
			using FileStream file = File.OpenRead(path);
			if (file.Length > 128 * 1024 * 1024) throw new IOException("Web asset exceeds 128 MiB.");
			await WriteHeader(stream, 200, ContentType(path), file.Length).ConfigureAwait(false);
			if (!head) await file.CopyToAsync(stream, 65536, m_cancel.Token).ConfigureAwait(false);
		}

		private async Task Write(NetworkStream stream, ModWebResponse response)
		{
			byte[] body = response.Body ?? Array.Empty<byte>();
			await WriteHeader(stream, response.Status, response.ContentType, body.Length).ConfigureAwait(false);
			await stream.WriteAsync(body, 0, body.Length, m_cancel.Token).ConfigureAwait(false);
		}

		private Task WriteHeader(NetworkStream stream, int status, string contentType, long length)
		{
			if (string.IsNullOrEmpty(contentType) || contentType.IndexOfAny(new[] { '\r', '\n' }) >= 0) contentType = "application/octet-stream";
			string header = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + (status == 200 ? "OK" : "Error") + "\r\n" +
				"Content-Type: " + contentType + "\r\nContent-Length: " + length.ToString(CultureInfo.InvariantCulture) +
				"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
				"Cross-Origin-Resource-Policy: same-origin\r\nX-Frame-Options: SAMEORIGIN\r\n\r\n";
			byte[] bytes = Encoding.ASCII.GetBytes(header);
			return stream.WriteAsync(bytes, 0, bytes.Length, m_cancel.Token);
		}

		private static string ContentType(string path)
		{
			switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
			{
			case ".html": return "text/html; charset=utf-8";
			case ".js": case ".mjs": return "text/javascript; charset=utf-8";
			case ".css": return "text/css; charset=utf-8";
			case ".wasm": return "application/wasm";
			case ".json": return "application/json; charset=utf-8";
			case ".png": return "image/png";
			case ".svg": return "image/svg+xml";
			case ".woff2": return "font/woff2";
			case ".ttf": return "font/ttf";
			case ".wgsl": case ".ftl": case ".txt": return "text/plain; charset=utf-8";
			default: return "application/octet-stream";
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref m_disposed, 1) != 0) return;
			m_cancel.Cancel();
			m_listener.Stop();
			while (m_mainThread.TryDequeue(out Action _)) { }
			lock (m_requests)
			{
				foreach (Pending pending in m_requests.Values)
					pending.Result.TrySetResult(ModWebResponse.Json("{\"error\":\"Mod disabled\"}", 503));
				m_requests.Clear();
			}
			if (m_runner != null) UnityEngine.Object.Destroy(m_runner);
		}
	}

	internal sealed class ModWebDispatcher : MonoBehaviour
	{
		internal ModWebService Service;
		private void Update() { Service?.Pump(); }
		private void OnDestroy() { Service?.Dispose(); }
	}
}
