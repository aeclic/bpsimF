// DshBridge.cs — DeepSeek Harness -> Unity Editor 桥接层(多实例版 v0.2)
// 安装:把本文件放到任意 Unity 项目的 Assets/Editor/ 下,打开项目即自动启动。
// 服务:HTTP 127.0.0.1:<自动分配端口,默认从 8790 起找空位>
//   GET  /ping                存活探针(工作线程直接应答,编译中也可用;自报 projectId/port)
//   GET  /status              状态 + 日志 + 最新截图
//   GET  /file?path=<相对路径>  读取项目内文件(png/log/txt/cs/json/meta)
//   GET  /shot/latest         最新截图 PNG
//   POST /api {method, params}  在主线程执行 Unity API,返回 {ok, result | error}
//
// 多工程共存约定(同一编辑器版本常同时开多个工程副本):
//   1) 端口不写死:显式 DSH_BRIDGE_PORT 时严格使用;否则 8790-8799 里找空闲端口,
//      再不行让系统分配。绝不静默复用别人已占用的端口。
//   2) 每个工程落地两份自述文件,内容一致:
//        <project>/Temp/DshBridge/instance.json   (随工程走,slot 场景下最可靠)
//        %LOCALAPPDATA%/DshBridge/instances/<projectId>.json  (全局,用于发现所有运行中的编辑器)
//      心跳 4s 刷新一次;进程退出/正常关闭时删除;启动时顺手清理 pid 已死的残留条目。
//   3) 身份校验:请求头 X-Dsh-Project 或 /api 请求体里的 "project" 字段(projectId 或
//      工程绝对路径)与本地不符时返回 409,绝不执行——防止 agent 把命令发进"另一个工程"。
//   4) EditorPrefs / SessionState 键按 projectId 加命名空间:它们是整个编辑器版本共享的,
//      不加前缀时多工程会互相串设置。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DshBridge
{
    [InitializeOnLoad]
    public static class DshBridgeServer
    {
        // ---------- 实例身份 / 端口 ----------
        // 端口不写死:同机多工程(副本、兄弟工程)会撞端口,撞了就有一条命令发进别的工程。
        public const string Version = "0.2";
        public const string ServerName = "DshBridge v0.2";
        const int PortScanFrom = 8790;
        const int PortScanCount = 10;

        static string _projectId = "";
        static int _port = 0;
        static string _instanceDir;
        static string _instanceFile;
        static string _portFile;
        static string _registryDir;
        static string _registryFile;
        static string _startedAt = "";
        static long _lastInstanceWrite = -1;
        static string _unityVersion = "";
        static bool _batchMode;

        public static int Port { get { return _port; } }
        public static string ProjectId { get { return _projectId; } }
        public static string ProjectPath { get { return _projectPath; } }

        public static string RegistryDir
        {
            get
            {
                if (_registryDir == null)
                    _registryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshBridge", "instances");
                return _registryDir;
            }
        }

        // 路径哈希:FNV-1a 64 位取前 12 位十六进制。跨进程稳定,不用引 System.Security.Cryptography。
        static string ShortHash(string s)
        {
            ulong h = 14695981039346656037UL;
            string lower = s.ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++) { h ^= lower[i]; h *= 1099511628211UL; }
            return h.ToString("x16").Substring(0, 12);
        }

        static void InitIdentity()
        {
            if (string.IsNullOrEmpty(_projectPath))
                _projectPath = Directory.GetParent(Application.dataPath).FullName;
            _projectPath = _projectPath.TrimEnd('\\', '/');
            _projectId = ShortHash(_projectPath);
            // 这两个值会被工作线程(/ping)读取,Unity API 不能跨线程碰,主线程取好缓存。
            _unityVersion = Application.unityVersion;
            _batchMode = Application.isBatchMode;
            _instanceDir = Path.Combine(_projectPath, "Temp", "DshBridge");
            _instanceFile = Path.Combine(_instanceDir, "instance.json");
            _portFile = Path.Combine(_instanceDir, "port.txt");
            _registryFile = Path.Combine(RegistryDir, _projectId + ".json");
            if (string.IsNullOrEmpty(_startedAt)) _startedAt = DateTime.UtcNow.ToString("o");
        }

        // 端口上真的有人在监听?能连上就算占用(含被 kill 后残留的孤儿 socket)。
        static bool PortIsOpen(int p)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(IPAddress.Loopback, p, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(250)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        // 上次这个工程用的端口优先复用:域重载/编译器刷新后端口不跳动,agent 少踩一次"端口变了"。
        static int PreviousPort()
        {
            try
            {
                if (!File.Exists(_instanceFile)) return 0;
                Dictionary<string, object> d = Json.Parse(File.ReadAllText(_instanceFile)) as Dictionary<string, object>;
                if (d == null || !d.ContainsKey("project")) return 0;
                string owner = d["project"].ToString().TrimEnd('\\', '/');
                if (!string.Equals(owner, _projectPath, StringComparison.OrdinalIgnoreCase)) return 0;
                return Convert.ToInt32(d.ContainsKey("port") ? d["port"] : 0);
            }
            catch { return 0; }
        }

        static TcpListener BindListener(out int port, out string error)
        {
            error = null;
            port = 0;
            string raw = Environment.GetEnvironmentVariable("DSH_BRIDGE_PORT");
            int forced = 0;
            bool hasForced = !string.IsNullOrEmpty(raw) && int.TryParse(raw, out forced) && forced > 0 && forced < 65536;
            List<int> candidates = new List<int>();
            if (hasForced) candidates.Add(forced);
            else
            {
                int prev = PreviousPort();
                if (prev > 0) candidates.Add(prev);
                for (int i = 0; i < PortScanCount; i++) candidates.Add(PortScanFrom + i);
                candidates.Add(0); // 全被占就让系统分配,至少能起来
            }
            foreach (int p in candidates)
            {
                if (p != 0 && PortIsOpen(p)) continue;
                try
                {
                    TcpListener l = new TcpListener(IPAddress.Loopback, p);
                    // 独占:避免两个编辑器同时"绑定成功"到同一端口后再互相抢连接。
                    l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
                    l.Start();
                    port = ((IPEndPoint)l.LocalEndpoint).Port;
                    return l;
                }
                catch (Exception e)
                {
                    if (hasForced)
                    {
                        error = "端口 " + p + " 不可用（DSH_BRIDGE_PORT 显式指定，不会自动换端口）：" + e.Message;
                        break;
                    }
                }
            }
            if (error == null) error = "没有可用端口（" + PortScanFrom + "-" + (PortScanFrom + PortScanCount - 1) + " 均被占用，系统分配也失败）";
            return null;
        }

        static Dictionary<string, object> InstanceData(int port, string error)
        {
            return new Dictionary<string, object>
            {
                { "bridge", "DshBridge" },
                { "version", Version },
                { "project", _projectPath },
                { "projectId", _projectId },
                { "port", port },
                { "pid", Process.GetCurrentProcess().Id },
                { "unity", _unityVersion },
                { "batchMode", _batchMode },
                { "startedAt", _startedAt },
                { "updatedAt", DateTime.UtcNow.ToString("o") },
                { "instanceFile", _instanceFile },
                { "registryFile", _registryFile },
                { "log", _logFile },
                { "error", error },
            };
        }

        static void WriteAtomic(string file, string text)
        {
            try
            {
                string tmp = file + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                File.WriteAllText(tmp, text);
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch { }
        }

        static void WriteInstanceFiles(int port, string error)
        {
            try
            {
                Directory.CreateDirectory(_instanceDir);
                Directory.CreateDirectory(RegistryDir);
                string json = Json.Serialize(InstanceData(port, error));
                WriteAtomic(_instanceFile, json);
                WriteAtomic(_registryFile, json);
                WriteAtomic(_portFile, port > 0 ? port.ToString() : "");
                _lastInstanceWrite = Watch.ElapsedMilliseconds;
            }
            catch (Exception e) { Log("write instance files failed: " + e.Message); }
        }

        static void RemoveInstanceFiles()
        {
            try { if (_instanceFile != null && File.Exists(_instanceFile)) File.Delete(_instanceFile); } catch { }
            try { if (_portFile != null && File.Exists(_portFile)) File.Delete(_portFile); } catch { }
            try { if (_registryFile != null && File.Exists(_registryFile)) File.Delete(_registryFile); } catch { }
        }

        static bool PidAlive(int pid)
        {
            if (pid <= 0) return false;
            try { Process p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        // 启动时清理:pid 已死(崩溃/被 kill)的其它工程条目留着会误导客户端。
        static void SweepRegistry()
        {
            try
            {
                if (!Directory.Exists(RegistryDir)) return;
                foreach (string f in Directory.GetFiles(RegistryDir, "*.json"))
                {
                    if (_registryFile != null && string.Equals(f, _registryFile, StringComparison.OrdinalIgnoreCase)) continue;
                    bool dead = true;
                    try
                    {
                        Dictionary<string, object> d = Json.Parse(File.ReadAllText(f)) as Dictionary<string, object>;
                        dead = d == null || !PidAlive(Convert.ToInt32(d.ContainsKey("pid") ? d["pid"] : 0));
                    }
                    catch { dead = true; }
                    if (dead) { try { File.Delete(f); Log("sweep dead instance " + Path.GetFileName(f)); } catch { } }
                }
            }
            catch { }
        }

        static void OnQuitting()
        {
            RemoveInstanceFiles();
        }
        const int MaxBodyBytes = 8 * 1024 * 1024;
        const int DispatchTimeoutMs = 60000;
        const int MaxLogs = 400;

        static TcpListener _listener;
        static Thread _acceptThread;
        static volatile bool _stopping;
        static readonly object QueueLock = new object();
        static readonly Queue<PendingRequest> Pending = new Queue<PendingRequest>();
        static readonly List<LogEntry> Logs = new List<LogEntry>();
        static readonly object LogLock = new object();
        static string _projectPath;
        static string _logFile;
        static string _taskStatus = "";
        static double _taskUpdated = 0;
        static readonly Stopwatch Watch = Stopwatch.StartNew();
        // C# forbids `volatile` on long (CS0677); cross-thread access goes through Volatile.Read/Write.
        static long _lastMainTickMs;
        static volatile bool _cachedPlaying;
        static volatile bool _cachedChanging;
        static volatile bool _cachedCompiling;

        class PendingRequest { public string Raw; public bool IsPing; public bool IsStatus; public Action<string> Respond; }
        class LogEntry { public string Message; public string Stack; public string Type; public double Time; }
        public class LogInfo { public string Type; public string Message; public double Time; }

        static DshBridgeServer()
        {
            // Start right away: in this project a queued delayCall from the [InitializeOnLoad]
            // static constructor never ran (no listener, no bridge.log), and after every
            // script recompile the domain reload re-runs this constructor while the old
            // listener is already stopped - so the bridge has to come back by itself.
            try { Start(); } catch { }
            EditorApplication.delayCall += Start; // harmless retry, Start() is idempotent
            Application.logMessageReceived += OnLog;
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
            try { EditorApplication.quitting += OnQuitting; } catch { }
        }

        static void OnDomainUnload(object sender, EventArgs e)
        {
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        [MenuItem("DshBridge/Start Server")]
        public static void Start()
        {
            try
            {
                if (_listener != null && _acceptThread != null && _acceptThread.IsAlive) return;
                InitIdentity();
                Directory.CreateDirectory(_instanceDir);
                _logFile = Path.Combine(_instanceDir, "bridge.log");
                Log("== boot pid=" + Process.GetCurrentProcess().Id + " project=" + _projectPath +
                    " id=" + _projectId + " unity=" + Application.unityVersion + " batch=" + Application.isBatchMode);
                SweepRegistry();

                int port;
                string error;
                TcpListener listener = BindListener(out port, out error);
                if (listener == null)
                {
                    Log("bind failed: " + error);
                    WriteInstanceFiles(-1, error);
                    return;
                }
                _port = port;
                _listener = listener;
                _stopping = false;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DshBridgeAcceptor" };
                _acceptThread.Start();
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                Log("server started on 127.0.0.1:" + _port);
                WriteInstanceFiles(_port, null);
                // Do NOT auto-open the status window.
                // A custom EditorWindow that is open when the editor saves its layout gets written
                // into <layout>.dwlt; on the next startup Unity cannot resolve the class early
                // enough, logs "[LAYOUT] Removed un-parented EditorWindow ... FallbackEditorWindow"
                // and then warns "The layout ... could not be fully loaded". Keeping the window
                // closed by default keeps the console clean. Open it from the menu when wanted:
                // DshBridge/Status Window. (Opt back in with EditorPrefs key below.)
                bool autoOpen = false;
                try { autoOpen = EditorPrefs.GetBool(PrefKey("AutoOpenStatusWindow"), false); } catch { }
                if (autoOpen && !_batchMode && !SessionState.GetBool(PrefKey("WindowOpened"), false))
                {
                    SessionState.SetBool(PrefKey("WindowOpened"), true);
                    EditorApplication.delayCall += delegate { DshBridgeWindow.ShowWindow(); };
                }
            }
            catch (Exception e)
            {
                Log("start failed: " + e);
            }
        }

        public static void Stop()
        {
            _stopping = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
            _port = 0;
            EditorApplication.update -= Update;
            RemoveInstanceFiles();
        }

        public static void Restart() { Stop(); Start(); }

        public static bool IsRunning { get { return _listener != null; } }

        public static string CurrentTask { get { return _taskStatus; } }

        public static string LatestScreenshotPath
        {
            get
            {
                try
                {
                    string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                    if (Directory.Exists(dir))
                    {
                        string[] files = Directory.GetFiles(dir, "shot_*.png");
                        if (files.Length > 0) { Array.Sort(files); return files[files.Length - 1]; }
                    }
                }
                catch { }
                return null;
            }
        }

        public static List<LogInfo> GetRecentLogs(int count)
        {
            List<LogInfo> result = new List<LogInfo>();
            lock (LogLock)
            {
                int start = Math.Max(0, Logs.Count - count);
                for (int i = start; i < Logs.Count; i++)
                {
                    LogEntry e = Logs[i];
                    result.Add(new LogInfo { Type = e.Type, Message = e.Message, Time = e.Time });
                }
            }
            return result;
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            lock (LogLock)
            {
                Logs.Add(new LogEntry { Message = condition, Stack = stack, Type = type.ToString(), Time = Time.realtimeSinceStartupAsDouble });
                if (Logs.Count > MaxLogs) Logs.RemoveRange(0, Logs.Count - MaxLogs);
            }
        }

        static void Log(string text)
        {
            try
            {
                if (_logFile == null) return;
                File.AppendAllText(_logFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + "\r\n");
            }
            catch { }
        }

        // EditorPrefs / SessionState 是"整个编辑器版本"共享的:多工程不加前缀会互相串设置。
        static string PrefKey(string key) { return "DshBridge." + _projectId + "." + key; }

        static string HeaderValue(string[] lines, string name)
        {
            foreach (string line in lines)
            {
                int i = line.IndexOf(':');
                if (i <= 0) continue;
                if (line.Substring(0, i).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(i + 1).Trim();
            }
            return null;
        }

        static bool ProjectMatches(string want)
        {
            if (string.IsNullOrEmpty(want)) return true;
            string w = want.Trim().Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrEmpty(w)) return false;
            return string.Equals(w, _projectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w, _projectPath, StringComparison.OrdinalIgnoreCase);
        }

        static string WrongProjectJson()
        {
            return Json.Serialize(new Dictionary<string, object>
            {
                { "ok", false },
                { "projectError", true },
                { "error", "项目不匹配：本实例是 " + _projectPath + "（projectId=" + _projectId + "），请求的目标不是它" },
                { "project", _projectPath },
                { "projectId", _projectId },
                { "port", _port },
            });
        }

        static void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client = null;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (_stopping) break; Thread.Sleep(200); continue; }
                ThreadPool.QueueUserWorkItem(delegate { HandleClient(client); });
            }
        }

        static void HandleClient(TcpClient client)
        {
            try
            {
                using (NetworkStream s = client.GetStream())
                {
                    s.ReadTimeout = 30000;
                    string header = ReadUntil(s, "\r\n\r\n");
                    if (header.Length == 0) return;
                    string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] parts = lines[0].Split(' ');
                    string method = parts[0];
                    string url = parts.Length > 1 ? parts[1] : "/";
                    int contentLength = 0;
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(line.Split(':')[1].Trim(), out contentLength);
                        }
                    }
                    if (contentLength > MaxBodyBytes) { Respond(s, "413 Request Too Large", Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"body too large\"}")); return; }
                    byte[] body = new byte[contentLength];
                    if (contentLength > 0)
                    {
                        int off = 0;
                        while (off < contentLength) { int r = s.Read(body, off, contentLength - off); if (r <= 0) return; off += r; }
                    }

                    // 身份校验:调用方可带 X-Dsh-Project(工程绝对路径或 projectId)。
                    // 不符时直接 409 —— 宁可让 agent 报错,也不能把命令执行进别的工程。
                    string wantProject = HeaderValue(lines, "X-Dsh-Project");
                    if (!string.IsNullOrEmpty(wantProject) && !ProjectMatches(wantProject))
                    {
                        Respond(s, "409 Conflict", Encoding.UTF8.GetBytes(WrongProjectJson()));
                        return;
                    }

                    string status = "200 OK";
                    byte[] respBody;
                    string contentType = "application/json; charset=utf-8";
                    if (method == "GET" && url.StartsWith("/ping"))
                    {
                        respBody = Encoding.UTF8.GetBytes(WorkerPing());
                    }
                    else if (method == "GET" && (url == "/" || url.StartsWith("/?")))
                    {
                        contentType = "text/html; charset=utf-8";
                        respBody = Encoding.UTF8.GetBytes(DashboardHtml());
                    }
                    else if (method == "GET" && url.StartsWith("/status"))
                    {
                        respBody = Encoding.UTF8.GetBytes(WorkerStatus());
                    }
                    else if (method == "GET" && url.StartsWith("/shot/latest"))
                    {
                        respBody = ServeLatestShot(out contentType, out status);
                    }
                    else if (method == "GET" && url.StartsWith("/file"))
                    {
                        string query = url.IndexOf('?') >= 0 ? url.Substring(url.IndexOf('?') + 1) : "";
                        respBody = ServeFile(ExtractQuery(query, "path"), out contentType, out status);
                    }
                    else if (method == "POST" && url.StartsWith("/api"))
                    {
                        respBody = Encoding.UTF8.GetBytes(DispatchOnMainThread(Encoding.UTF8.GetString(body)));
                    }
                    else
                    {
                        status = "404 Not Found";
                        respBody = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"not found\"}");
                    }
                    Respond(s, status, respBody, contentType);
                }
            }
            catch (Exception e) { Log("handle client error: " + e.Message); }
            finally { try { client.Close(); } catch { } }
        }

        static void Respond(NetworkStream s, string status, byte[] body, string contentType = "application/json; charset=utf-8")
        {
            string head = "HTTP/1.1 " + status + "\r\n" +
                          "Content-Type: " + contentType + "\r\n" +
                          "Content-Length: " + body.Length + "\r\n" +
                          "Connection: close\r\n" +
                          "Access-Control-Allow-Origin: *\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            s.Write(headBytes, 0, headBytes.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        static string ReadUntil(NetworkStream s, string marker)
        {
            using (MemoryStream buf = new MemoryStream())
            {
                int matched = 0;
                while (true)
                {
                    int b = s.ReadByte();
                    if (b < 0) throw new EndOfStreamException();
                    buf.WriteByte((byte)b);
                    char c = (char)b;
                    if (c == marker[matched]) { matched++; if (matched == marker.Length) break; }
                    else matched = (c == marker[0]) ? 1 : 0;
                }
                byte[] all = buf.ToArray();
                return Encoding.UTF8.GetString(all, 0, all.Length - marker.Length);
            }
        }

        static string ExtractQuery(string query, string key)
        {
            foreach (string kv in query.Split('&'))
            {
                string[] pair = kv.Split('=');
                if (pair.Length == 2 && pair[0] == key) return Uri.UnescapeDataString(pair[1]);
            }
            return null;
        }

        static byte[] ServeFile(string relPath, out string contentType, out string status)
        {
            contentType = "application/octet-stream";
            status = "404 Not Found";
            if (string.IsNullOrEmpty(relPath)) return Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"missing path\"}");
            string full = Path.GetFullPath(Path.Combine(_projectPath, relPath));
            if (!full.StartsWith(_projectPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                return Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"file not found\"}");
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == ".png") contentType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") contentType = "image/jpeg";
            else if (ext == ".log" || ext == ".txt" || ext == ".cs" || ext == ".json" || ext == ".md" || ext == ".meta") contentType = "text/plain; charset=utf-8";
            status = "200 OK";
            return File.ReadAllBytes(full);
        }

        static byte[] ServeLatestShot(out string contentType, out string status)
        {
            contentType = "image/png";
            status = "200 OK";
            try
            {
                string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "shot_*.png");
                    if (files.Length > 0)
                    {
                        Array.Sort(files);
                        return File.ReadAllBytes(files[files.Length - 1]);
                    }
                }
            }
            catch { }
            status = "404 Not Found";
            return new byte[0];
        }

        static string DashboardHtml()
        {
            return @"<!doctype html>
<html lang='zh'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>DSH ↔ Unity 桥接状态</title>
<style>
  *{box-sizing:border-box}
  body{margin:0;font-family:-apple-system,'Segoe UI','Microsoft YaHei',sans-serif;background:#14161a;color:#e6e6e6}
  header{padding:14px 20px;background:#1d2026;border-bottom:1px solid #2a2e37;display:flex;align-items:center;gap:12px;flex-wrap:wrap}
  .dot{width:14px;height:14px;border-radius:50%;background:#666;flex:none}
  .dot.on{background:#35c26a;box-shadow:0 0 8px #35c26a}
  .dot.busy{background:#f0b429;box-shadow:0 0 8px #f0b429}
  .dot.off{background:#e5484d}
  h1{font-size:16px;margin:0;font-weight:600}
  .meta{color:#8b93a1;font-size:12px}
  .chips{display:flex;gap:8px;margin-left:auto;flex-wrap:wrap}
  .chip{padding:4px 10px;border-radius:20px;font-size:12px;background:#2a2e37;color:#8b93a1}
  .chip.on{background:#13381f;color:#35c26a}
  .chip.warn{background:#3a2e08;color:#f0b429}
  .task{padding:12px 20px;font-size:14px;border-bottom:1px solid #2a2e37}
  .task b{color:#58a6ff;font-weight:600}
  main{display:flex;height:calc(100vh - 118px)}
  .shot{flex:1;display:flex;align-items:center;justify-content:center;background:#0c0d10;min-width:0}
  .shot img{max-width:100%;max-height:100%;object-fit:contain}
  .logs{width:440px;overflow-y:auto;background:#17191e;border-left:1px solid #2a2e37;padding:8px 10px;font-family:Consolas,Menlo,monospace;font-size:12px}
  .log{padding:3px 0;border-bottom:1px solid #20232a;white-space:pre-wrap;word-break:break-all}
  .log.error{color:#f0777a}
  .log.warning{color:#e5b567}
  .log.info{color:#9aa4b2}
  .empty{color:#4b5260;text-align:center;padding:30px 10px}
</style>
</head>
<body>
<header>
  <div class='dot' id='dot'></div>
  <div>
    <h1>DSH ↔ Unity 桥接状态</h1>
    <div class='meta' id='meta'>连接中…</div>
  </div>
  <div class='chips'>
    <span class='chip' id='c_compiling'>编译中</span>
    <span class='chip' id='c_playing'>播放中</span>
    <span class='chip' id='c_changing'>域重载</span>
  </div>
</header>
<div class='task' id='task'>—</div>
<main>
  <div class='shot'><img id='shot' alt='Unity 最新截图'></div>
  <div class='logs' id='logs'><div class='empty'>等待日志…</div></div>
</main>
<script>
let lastShot = '';
async function tick(){
  try{
    const r = await fetch('/status', {cache:'no-store'});
    const s = await r.json();
    const dot = document.getElementById('dot');
    const busy = s.busy || s.compiling || s.changing;
    dot.className = 'dot ' + (s.ok ? (busy ? 'busy' : 'on') : 'off');
    document.getElementById('meta').textContent = s.ok
      ? ('Unity ' + s.unity + ' · ' + s.project + ' · ' + (busy ? '忙(请稍候)' : '就绪'))
      : '离线';
    setChip('c_compiling', s.compiling);
    setChip('c_playing', s.playing);
    setChip('c_changing', s.changing);
    document.getElementById('task').innerHTML = s.task
      ? ('<b>当前任务:</b> ' + s.task)
      : '<b>当前任务:</b> 空闲';
    if(s.screenshot && s.screenshot !== lastShot){
      lastShot = s.screenshot;
      document.getElementById('shot').src = '/shot/latest?t=' + Date.now();
    }
    renderLogs(s.logs || []);
  }catch(e){
    document.getElementById('dot').className = 'dot off';
    document.getElementById('meta').textContent = '连接断开，正在重试…';
  }
}
function setChip(id, on){ document.getElementById(id).className = 'chip' + (on ? ' warn' : ''); }
function renderLogs(logs){
  const box = document.getElementById('logs');
  if(!logs.length){ box.innerHTML = '<div class=\'empty\'>暂无日志</div>'; return; }
  box.innerHTML = '';
  for(const l of logs){
    const d = document.createElement('div');
    d.className = 'log ' + (l.type === 'Error' ? 'error' : l.type === 'Warning' ? 'warning' : 'info');
    const t = (typeof l.time === 'number') ? l.time.toFixed(1) : '';
    d.textContent = t + ' ' + l.message;
    box.appendChild(d);
  }
  box.scrollTop = box.scrollHeight;
}
tick();
setInterval(tick, 1500);
</script>
</body>
</html>";
        }

        static Dictionary<string, object> PingInfo()
        {
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "server", ServerName },
                { "unity", _unityVersion },
                { "project", _projectPath },
                { "projectId", _projectId },
                { "port", _port },
                { "pid", Process.GetCurrentProcess().Id },
                { "batchMode", _batchMode },
                { "playing", EditorApplication.isPlaying },
                { "changing", EditorApplication.isPlayingOrWillChangePlaymode },
                { "compiling", EditorApplication.isCompiling },
            };
        }

        static Dictionary<string, object> StatusInfo()
        {
            Dictionary<string, object> info = PingInfo();
            if (string.IsNullOrEmpty(_taskStatus))
            {
                try
                {
                    string tf = Path.Combine(_projectPath, "Temp", "DshBridge", "task.txt");
                    if (File.Exists(tf)) _taskStatus = File.ReadAllText(tf);
                }
                catch { }
            }
            info["task"] = _taskStatus;
            info["taskUpdated"] = _taskUpdated;
            List<object> logs = new List<object>();
            lock (LogLock)
            {
                int start = Math.Max(0, Logs.Count - 25);
                for (int i = start; i < Logs.Count; i++)
                {
                    LogEntry e = Logs[i];
                    logs.Add(new Dictionary<string, object> { { "type", e.Type }, { "message", e.Message }, { "time", e.Time } });
                }
            }
            info["logs"] = logs;
            string shot = "";
            try
            {
                string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "shot_*.png");
                    if (files.Length > 0)
                    {
                        Array.Sort(files);
                        shot = "Temp/DshBridge/" + Path.GetFileName(files[files.Length - 1]);
                    }
                }
            }
            catch { }
            info["screenshot"] = shot;
            return info;
        }

        static string WorkerPing()
        {
            bool stalled = Watch.ElapsedMilliseconds - Volatile.Read(ref _lastMainTickMs) > 3000;
            return Json.Serialize(new Dictionary<string, object>
            {
                { "ok", true },
                { "server", ServerName },
                { "unity", _unityVersion },
                { "project", _projectPath },
                { "projectId", _projectId },
                { "port", _port },
                { "pid", Process.GetCurrentProcess().Id },
                { "batchMode", _batchMode },
                { "playing", _cachedPlaying },
                { "changing", _cachedChanging },
                { "compiling", _cachedCompiling },
                { "busy", stalled || _cachedChanging || _cachedCompiling },
                { "mainThreadStalled", stalled },
            });
        }

        static string WorkerStatus()
        {
            bool stalled = Watch.ElapsedMilliseconds - Volatile.Read(ref _lastMainTickMs) > 3000;
            Dictionary<string, object> info = new Dictionary<string, object>
            {
                { "ok", true },
                { "server", ServerName },
                { "unity", _unityVersion },
                { "project", _projectPath },
                { "projectId", _projectId },
                { "port", _port },
                { "pid", Process.GetCurrentProcess().Id },
                { "batchMode", _batchMode },
                { "playing", _cachedPlaying },
                { "changing", _cachedChanging },
                { "compiling", _cachedCompiling },
                { "busy", stalled || _cachedChanging || _cachedCompiling },
                { "mainThreadStalled", stalled },
            };
            if (string.IsNullOrEmpty(_taskStatus))
            {
                try
                {
                    string tf = Path.Combine(_projectPath, "Temp", "DshBridge", "task.txt");
                    if (File.Exists(tf)) _taskStatus = File.ReadAllText(tf);
                }
                catch { }
            }
            info["task"] = _taskStatus;
            info["taskUpdated"] = _taskUpdated;
            List<object> logs = new List<object>();
            lock (LogLock)
            {
                int start = Math.Max(0, Logs.Count - 25);
                for (int i = start; i < Logs.Count; i++)
                {
                    LogEntry e = Logs[i];
                    logs.Add(new Dictionary<string, object> { { "type", e.Type }, { "message", e.Message }, { "time", e.Time } });
                }
            }
            info["logs"] = logs;
            string shot = "";
            try
            {
                string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "shot_*.png");
                    if (files.Length > 0)
                    {
                        Array.Sort(files);
                        shot = "Temp/DshBridge/" + Path.GetFileName(files[files.Length - 1]);
                    }
                }
            }
            catch { }
            info["screenshot"] = shot;
            return Json.Serialize(info);
        }

        static string DispatchOnMainThread(string body)
        {
            if (Watch.ElapsedMilliseconds - Volatile.Read(ref _lastMainTickMs) > 5000)
                return "{\"ok\":false,\"error\":\"editor main thread stalled (window minimized/backgrounded?)\"}";
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            string result = null;
            lock (QueueLock) { Pending.Enqueue(new PendingRequest { Raw = body, Respond = r => { result = r; done.Set(); } }); }
            if (!done.Wait(DispatchTimeoutMs))
                return "{\"ok\":false,\"error\":\"timeout: editor main thread busy (compiling?)\"}";
            return result;
        }

        static string DispatchPing()
        {
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            string result = null;
            lock (QueueLock) { Pending.Enqueue(new PendingRequest { IsPing = true, Respond = r => { result = r; done.Set(); } }); }
            if (!done.Wait(2500))
                return "{\"ok\":true,\"server\":\"DshBridge v0.1\",\"busy\":true,\"compiling\":true,\"changing\":true,\"playing\":true}";
            return result;
        }

        static string DispatchStatus()
        {
            ManualResetEventSlim done = new ManualResetEventSlim(false);
            string result = null;
            lock (QueueLock) { Pending.Enqueue(new PendingRequest { IsStatus = true, Respond = r => { result = r; done.Set(); } }); }
            if (!done.Wait(2500))
                return "{\"ok\":true,\"server\":\"DshBridge v0.1\",\"busy\":true,\"compiling\":true,\"changing\":true,\"playing\":true,\"task\":\"(编辑器忙/编译中)\",\"logs\":[],\"screenshot\":\"\"}";
            return result;
        }

        static void Update()
        {
            _cachedPlaying = EditorApplication.isPlaying;
            _cachedChanging = EditorApplication.isPlayingOrWillChangePlaymode;
            _cachedCompiling = EditorApplication.isCompiling;
            Volatile.Write(ref _lastMainTickMs, Watch.ElapsedMilliseconds);
            // 心跳:让外部工具能判断"这个工程的 bridge 还活着",崩溃残留靠 pid 兜底。
            if (_listener != null && _port > 0 && Watch.ElapsedMilliseconds - _lastInstanceWrite > 4000)
                WriteInstanceFiles(_port, null);
            List<PendingRequest> batch = null;
            lock (QueueLock)
            {
                if (Pending.Count > 0) { batch = new List<PendingRequest>(Pending); Pending.Clear(); }
            }
            if (batch == null) return;
            foreach (PendingRequest req in batch)
            {
                try
                {
                    if (req.IsPing) req.Respond(Json.Serialize(PingInfo()));
                    else if (req.IsStatus) req.Respond(Json.Serialize(StatusInfo()));
                    else req.Respond(HandleApi(req.Raw));
                }
                catch (Exception e) { req.Respond("{\"ok\":false,\"error\":" + Json.Serialize(e.Message + "\n" + e.StackTrace) + "}"); }
            }
        }

        static string HandleApi(string body)
        {
            Dictionary<string, object> req = Json.Parse(body) as Dictionary<string, object>;
            if (req == null) return "{\"ok\":false,\"error\":\"invalid request\"}";
            string method = Commands.ToStr(Commands.Get(req, "method"), "");
            Dictionary<string, object> p = Commands.Get(req, "params") as Dictionary<string, object>;
            if (p == null) p = new Dictionary<string, object>();
            // 请求体里也可以声明目标工程(projectId 或绝对路径);不符时绝不执行。
            object want = req.ContainsKey("project") ? req["project"] : null;
            if (want != null && !ProjectMatches(want.ToString())) return WrongProjectJson();
            try
            {
                object result = Commands.Dispatch(method, p);
                return "{\"ok\":true,\"result\":" + Json.Serialize(result) + "}";
            }
            catch (Exception e)
            {
                return "{\"ok\":false,\"error\":" + Json.Serialize(method + " failed: " + e.Message) + "}";
            }
        }

        // ---------- 命令实现 ----------

        public static class Commands
        {
            public static object Dispatch(string method, Dictionary<string, object> p)
            {
                switch (method)
                {
                    case "ping": return PingInfo();
                    case "hierarchy": return CmdHierarchy(p);
                    case "find": return CmdFind(p);
                    case "create_primitive": return CmdCreatePrimitive(p);
                    case "create_empty": return CmdCreateEmpty(p);
                    case "destroy": return CmdDestroy(p);
                    case "set_transform": return CmdSetTransform(p);
                    case "set_active": return CmdSetActive(p);
                    case "add_component": return CmdAddComponent(p);
                    case "remove_component": return CmdRemoveComponent(p);
                    case "get_components": return CmdGetComponents(p);
                    case "set_property": return CmdSetProperty(p);
                    case "get_property": return CmdGetProperty(p);
                    case "write_script": return CmdWriteScript(p);
                    case "read_file": return CmdReadFile(p);
                    case "list_dir": return CmdListDir(p);
                    case "compile": return CmdCompile(p);
                    case "compile_status": return CmdCompileStatus(p);
                    case "set_status": return CmdSetStatus(p);
                    case "console": return CmdConsole(p);
                    case "profiler": return Bpsim.Perf.EditorProfiler.Read(p);
                    case "playmode": return CmdPlaymode(p);
                    case "playmode_state": return CmdPlaymodeState(p);
                    case "screenshot": return CmdScreenshot(p);
                    case "new_scene": return CmdNewScene(p);
                    case "save_scene": return CmdSaveScene(p);
                    case "open_scene": return CmdOpenScene(p);
                    case "invoke_static": return CmdInvokeStatic(p);
                    case "create_material": return CmdCreateMaterial(p);
                    case "quit": EditorApplication.Exit(0); return true;
                    default: throw new Exception("unknown method: " + method);
                }
            }

            static object CmdHierarchy(Dictionary<string, object> p)
            {
                Scene scene = SceneManager.GetActiveScene();
                List<object> roots = new List<object>();
                foreach (GameObject go in scene.GetRootGameObjects()) roots.Add(DumpGo(go, 0));
                return new Dictionary<string, object> { { "scene", scene.name }, { "path", scene.path }, { "roots", roots } };
            }

            static Dictionary<string, object> DumpGo(GameObject go, int depth)
            {
                List<object> comps = new List<object>();
                foreach (Component c in go.GetComponents<Component>()) { if (c != null) comps.Add(c.GetType().FullName); }
                List<object> children = new List<object>();
                if (depth < 6)
                {
                    foreach (Transform ch in go.transform) children.Add(DumpGo(ch.gameObject, depth + 1));
                }
                return new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "id", go.GetInstanceID() },
                    { "active", go.activeSelf },
                    { "position", Vec3Arr(go.transform.position) },
                    { "rotation", Vec3Arr(go.transform.rotation.eulerAngles) },
                    { "scale", Vec3Arr(go.transform.localScale) },
                    { "components", comps },
                    { "children", children },
                };
            }

            static object CmdFind(Dictionary<string, object> p)
            {
                string name = ToStr(Get(p, "name"), "");
                List<object> hits = new List<object>();
                foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    if (go != null && go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        hits.Add(new Dictionary<string, object> { { "name", go.name }, { "id", go.GetInstanceID() }, { "path", GetPath(go) } });
                }
                return hits;
            }

            static object CmdCreatePrimitive(Dictionary<string, object> p)
            {
                string type = ToStr(Get(p, "type"), "cube");
                string name = ToStr(Get(p, "name"), type);
                PrimitiveType pt = (PrimitiveType)Enum.Parse(typeof(PrimitiveType), type, true);
                GameObject go = GameObject.CreatePrimitive(pt);
                go.name = name;
                ApplyTransform(go, p);
                return GoRef(go);
            }

            static object CmdCreateEmpty(Dictionary<string, object> p)
            {
                string name = ToStr(Get(p, "name"), "GameObject");
                GameObject go = new GameObject(name);
                ApplyTransform(go, p);
                return GoRef(go);
            }

            static object CmdDestroy(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
                return true;
            }

            static object CmdSetTransform(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                if (p.ContainsKey("position")) go.transform.position = ToVec3(Get(p, "position"), go.transform.position);
                if (p.ContainsKey("rotation")) go.transform.rotation = Quaternion.Euler(ToVec3(Get(p, "rotation"), go.transform.rotation.eulerAngles));
                if (p.ContainsKey("scale")) go.transform.localScale = ToVec3(Get(p, "scale"), go.transform.localScale);
                if (p.ContainsKey("localPosition")) go.transform.localPosition = ToVec3(Get(p, "localPosition"), go.transform.localPosition);
                if (p.ContainsKey("localRotation")) go.transform.localRotation = Quaternion.Euler(ToVec3(Get(p, "localRotation"), go.transform.localRotation.eulerAngles));
                if (p.ContainsKey("parent"))
                {
                    object parentSpec = Get(p, "parent");
                    if (parentSpec == null) go.transform.SetParent(null);
                    else { GameObject parent = FindGameObject(parentSpec); if (parent == null) throw new Exception("parent not found"); go.transform.SetParent(parent.transform); }
                }
                return GoRef(go);
            }

            static object CmdSetActive(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                go.SetActive(ToBool(Get(p, "active"), true));
                return true;
            }

            static object CmdAddComponent(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                Type t = FindType(ToStr(Get(p, "type"), ""));
                if (t == null) throw new Exception("type not found: " + ToStr(Get(p, "type"), ""));
                Component existing = go.GetComponent(t);
                if (existing != null && !typeof(MonoBehaviour).IsAssignableFrom(t) && t != typeof(Transform))
                    return new Dictionary<string, object> { { "exists", true }, { "type", existing.GetType().FullName } };
                Component comp = go.AddComponent(t);
                return new Dictionary<string, object> { { "exists", false }, { "type", comp.GetType().FullName } };
            }

            static object CmdRemoveComponent(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                Type t = FindType(ToStr(Get(p, "type"), ""));
                if (t == null) throw new Exception("type not found");
                Component comp = go.GetComponent(t);
                if (comp == null) throw new Exception("component not present");
                if (Application.isPlaying) UnityEngine.Object.Destroy(comp);
                else UnityEngine.Object.DestroyImmediate(comp);
                return true;
            }

            static object CmdGetComponents(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                List<object> comps = new List<object>();
                foreach (Component c in go.GetComponents<Component>()) { if (c != null) comps.Add(c.GetType().FullName); }
                return comps;
            }

            static object CmdSetProperty(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                string compTypeName = ToStr(Get(p, "component"), "");
                string path = ToStr(Get(p, "path"), "");
                object value = Get(p, "value");
                object target = go;
                if (!string.IsNullOrEmpty(compTypeName))
                {
                    Type ct = FindType(compTypeName);
                    if (ct == null) throw new Exception("component type not found: " + compTypeName);
                    target = go.GetComponent(ct);
                    if (target == null) throw new Exception("component not present: " + compTypeName);
                }
                SetMemberValue(target, path, value);
                return true;
            }

            static object CmdGetProperty(Dictionary<string, object> p)
            {
                GameObject go = FindGameObject(Get(p, "target"));
                if (go == null) throw new Exception("target not found");
                string compTypeName = ToStr(Get(p, "component"), "");
                string path = ToStr(Get(p, "path"), "");
                object target = go;
                if (!string.IsNullOrEmpty(compTypeName))
                {
                    Type ct = FindType(compTypeName);
                    if (ct == null) throw new Exception("component type not found");
                    target = go.GetComponent(ct);
                    if (target == null) throw new Exception("component not present");
                }
                return ToJsonValue(GetMemberValue(target, path), go);
            }

            static object CmdWriteScript(Dictionary<string, object> p)
            {
                string rel = ToStr(Get(p, "path"), "");
                string content = ToStr(Get(p, "content"), "");
                if (string.IsNullOrEmpty(rel) || rel.IndexOf("..") >= 0 || !rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("path must be relative under Assets/");
                string full = Path.Combine(_projectPath, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, content, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                return new Dictionary<string, object> { { "path", rel }, { "bytes", content.Length } };
            }

            static object CmdReadFile(Dictionary<string, object> p)
            {
                string rel = ToStr(Get(p, "path"), "");
                string full = SafeProjectPath(rel);
                if (full == null || !File.Exists(full)) throw new Exception("file not found: " + rel);
                return File.ReadAllText(full);
            }

            static object CmdListDir(Dictionary<string, object> p)
            {
                string rel = ToStr(Get(p, "path"), "Assets");
                string full = SafeProjectPath(rel);
                if (full == null || !Directory.Exists(full)) throw new Exception("dir not found: " + rel);
                List<object> items = new List<object>();
                foreach (string f in Directory.GetFileSystemEntries(full))
                    items.Add(new Dictionary<string, object> {
                        { "name", Path.GetFileName(f) },
                        { "isDir", Directory.Exists(f) },
                    });
                return items;
            }

            static object CmdCompile(Dictionary<string, object> p)
            {
                CompilationPipeline.RequestScriptCompilation();
                return new Dictionary<string, object> { { "started", true } };
            }

            static object CmdCompileStatus(Dictionary<string, object> p)
            {
                int count = ToInt(Get(p, "count"), 80);
                List<object> errors = new List<object>();
                List<object> warnings = new List<object>();
                lock (LogLock)
                {
                    int start = Math.Max(0, Logs.Count - count);
                    for (int i = start; i < Logs.Count; i++)
                    {
                        LogEntry e = Logs[i];
                        Dictionary<string, object> entry = new Dictionary<string, object>
                        {
                            { "type", e.Type },
                            { "message", e.Message },
                            { "stack", e.Stack },
                            { "time", e.Time },
                        };
                        if (e.Type == "Error" || e.Message.IndexOf("error CS", StringComparison.OrdinalIgnoreCase) >= 0) errors.Add(entry);
                        else if (e.Type == "Warning" || e.Message.IndexOf("warning CS", StringComparison.OrdinalIgnoreCase) >= 0) warnings.Add(entry);
                    }
                }
                return new Dictionary<string, object>
                {
                    { "compiling", EditorApplication.isCompiling },
                    { "errors", errors },
                    { "warnings", warnings },
                };
            }

            static object CmdSetStatus(Dictionary<string, object> p)
            {
                string text = ToStr(Get(p, "text"), "");
                _taskStatus = text;
                _taskUpdated = Time.realtimeSinceStartupAsDouble;
                try
                {
                    string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "task.txt"), text);
                }
                catch { }
                return true;
            }

            static object CmdConsole(Dictionary<string, object> p)
            {
                int count = ToInt(Get(p, "count"), 50);
                List<object> items = new List<object>();
                lock (LogLock)
                {
                    int start = Math.Max(0, Logs.Count - count);
                    for (int i = start; i < Logs.Count; i++)
                    {
                        LogEntry e = Logs[i];
                        items.Add(new Dictionary<string, object> {
                            { "type", e.Type }, { "message", e.Message }, { "stack", e.Stack }, { "time", e.Time }
                        });
                    }
                }
                return items;
            }

            static object CmdPlaymode(Dictionary<string, object> p)
            {
                string action = ToStr(Get(p, "action"), "enter").ToLowerInvariant();
                if (action == "enter") EditorApplication.EnterPlaymode();
                else if (action == "exit") EditorApplication.ExitPlaymode();
                else if (action == "toggle") { if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else EditorApplication.EnterPlaymode(); }
                return PlaymodeState();
            }

            static object CmdPlaymodeState(Dictionary<string, object> p) { return PlaymodeState(); }

            static Dictionary<string, object> PlaymodeState()
            {
                return new Dictionary<string, object>
                {
                    { "playing", EditorApplication.isPlaying },
                    { "changing", EditorApplication.isPlayingOrWillChangePlaymode },
                };
            }

            static object CmdScreenshot(Dictionary<string, object> p)
            {
                int width = ToInt(Get(p, "width"), 640);
                int height = ToInt(Get(p, "height"), 480);
                float fov = ToFloat(Get(p, "fov"), 60f);
                Vector3 pos = ToVec3(Get(p, "position"), new Vector3(0f, 4f, -7f));
                Vector3 lookAt = ToVec3(Get(p, "lookAt"), new Vector3(0f, 1f, 0f));

                GameObject camGo = new GameObject("DshShotCam");
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.16f, 0.2f, 1f);
                cam.fieldOfView = fov;
                camGo.transform.position = pos;
                camGo.transform.LookAt(lookAt);

                RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                RenderTexture.active = null;

                if (Application.isPlaying) UnityEngine.Object.Destroy(camGo);
                else UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);

                string dir = Path.Combine(_projectPath, "Temp", "DshBridge");
                Directory.CreateDirectory(dir);
                int n = Directory.GetFiles(dir, "shot_*.png").Length;
                string fileName = "shot_" + n.ToString("D3") + ".png";
                string full = Path.Combine(dir, fileName);
                File.WriteAllBytes(full, png);
                return new Dictionary<string, object>
                {
                    { "path", "Temp/DshBridge/" + fileName },
                    { "abs", full },
                    { "width", width }, { "height", height }, { "bytes", png.Length },
                };
            }

            static object CmdNewScene(Dictionary<string, object> p)
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return new Dictionary<string, object> { { "scene", scene.name } };
            }

            static object CmdSaveScene(Dictionary<string, object> p)
            {
                string rel = ToStr(Get(p, "path"), "Assets/Scenes/Game.unity");
                if (rel.IndexOf("..") >= 0 || !rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) throw new Exception("path must be under Assets/");
                string full = Path.Combine(_projectPath, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), full);
                return new Dictionary<string, object> { { "path", rel } };
            }

            static object CmdOpenScene(Dictionary<string, object> p)
            {
                string rel = ToStr(Get(p, "path"), "");
                string full = SafeProjectPath(rel);
                if (full == null || !File.Exists(full)) throw new Exception("scene not found: " + rel);
                EditorSceneManager.OpenScene(full);
                return new Dictionary<string, object> { { "path", rel } };
            }

            static object CmdInvokeStatic(Dictionary<string, object> p)
            {
                string typeName = ToStr(Get(p, "type"), "");
                string methodName = ToStr(Get(p, "method"), "");
                List<object> rawArgs = Get(p, "args") as List<object>;
                if (rawArgs == null) rawArgs = new List<object>();
                Type t = FindType(typeName);
                if (t == null) throw new Exception("type not found: " + typeName);
                MethodInfo mi = null;
                try { mi = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static); }
                catch (AmbiguousMatchException)
                {
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        if (m.Name == methodName && m.GetParameters().Length == rawArgs.Count) { mi = m; break; }
                }
                if (mi == null) throw new Exception("static method not found: " + typeName + "." + methodName);
                ParameterInfo[] ps = mi.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) args[i] = Coerce(ps[i].ParameterType, rawArgs[i], null);
                object result;
                try
                {
                    result = mi.Invoke(null, args);
                }
                catch (TargetInvocationException exception)
                {
                    Exception inner = exception.InnerException ?? exception;
                    throw new Exception(inner.Message + "\n" + inner.StackTrace, inner);
                }
                return ToJsonValue(result, null);
            }

            static object CmdCreateMaterial(Dictionary<string, object> p)
            {
                string name = ToStr(Get(p, "name"), "Mat");
                string shaderName = ToStr(Get(p, "shader"), "Standard");
                Shader shader = Shader.Find(shaderName);
                if (shader == null) shader = Shader.Find("Standard");
                string rel = "Assets/Materials/" + name + ".mat";
                string full = Path.Combine(_projectPath, rel.Replace('/', Path.DirectorySeparatorChar));
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(rel);
                if (mat == null)
                {
                    mat = new Material(shader);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    AssetDatabase.CreateAsset(mat, rel);
                }
                if (p.ContainsKey("color")) mat.SetColor("_Color", ToColor(Get(p, "color"), Color.white));
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                return new Dictionary<string, object> { { "name", name }, { "path", rel } };
            }

            // ---------- 通用辅助 ----------

            static void ApplyTransform(GameObject go, Dictionary<string, object> p)
            {
                if (p.ContainsKey("position")) go.transform.position = ToVec3(Get(p, "position"), Vector3.zero);
                if (p.ContainsKey("rotation")) go.transform.rotation = Quaternion.Euler(ToVec3(Get(p, "rotation"), Vector3.zero));
                if (p.ContainsKey("scale")) go.transform.localScale = ToVec3(Get(p, "scale"), Vector3.one);
            }

            static string SafeProjectPath(string rel)
            {
                if (string.IsNullOrEmpty(rel) || rel.IndexOf("..") >= 0) return null;
                string full = Path.GetFullPath(Path.Combine(_projectPath, rel));
                if (!full.StartsWith(_projectPath, StringComparison.OrdinalIgnoreCase)) return null;
                return full;
            }

            static GameObject FindGameObject(object spec)
            {
                if (spec is double d) { UnityEngine.Object o = EditorUtility.InstanceIDToObject((int)d); return o as GameObject; }
                if (spec is int i2) { UnityEngine.Object o = EditorUtility.InstanceIDToObject(i2); return o as GameObject; }
                if (spec is string s)
                {
                    foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                        if (go != null && go.name == s) return go;
                }
                if (spec is Dictionary<string, object> dd && dd.ContainsKey("name")) return FindGameObject(ToStr(dd["name"], ""));
                return null;
            }

            static string GetPath(GameObject go)
            {
                string path = go.name;
                Transform parent = go.transform.parent;
                while (parent != null) { path = parent.name + "/" + path; parent = parent.parent; }
                return path;
            }

            static Dictionary<string, object> GoRef(GameObject go)
            {
                return new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "id", go.GetInstanceID() },
                    { "path", GetPath(go) },
                };
            }

            static Type FindType(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                Type exact = null;
                List<Type> candidates = new List<Type>();
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (Type t in types)
                    {
                        if (t.FullName == name) exact = t;
                        else if (t.Name == name) candidates.Add(t);
                    }
                }
                if (exact != null) return exact;
                foreach (Type t in candidates)
                    if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) return t;
                foreach (Type t in candidates)
                    if (t.Namespace != null && t.Namespace.StartsWith("UnityEditor")) return t;
                return candidates.Count > 0 ? candidates[0] : null;
            }

            static object GetMemberValue(object target, string path)
            {
                string[] segs = path.Split('.');
                object cur = target;
                for (int i = 0; i < segs.Length; i++)
                {
                    Type t = cur.GetType();
                    PropertyInfo pi = t.GetProperty(segs[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (pi != null) { cur = pi.GetValue(cur, null); continue; }
                    FieldInfo fi = t.GetField(segs[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (fi != null) { cur = fi.GetValue(cur); continue; }
                    throw new Exception("member not found: " + segs[i] + " on " + t.Name);
                }
                return cur;
            }

            static void SetMemberValue(object target, string path, object value)
            {
                string[] segs = path.Split('.');
                object cur = target;
                for (int i = 0; i < segs.Length - 1; i++)
                {
                    Type t = cur.GetType();
                    PropertyInfo pi = t.GetProperty(segs[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (pi != null) { cur = pi.GetValue(cur, null); continue; }
                    FieldInfo fi = t.GetField(segs[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (fi != null) { cur = fi.GetValue(cur); continue; }
                    throw new Exception("member not found: " + segs[i]);
                }
                string last = segs[segs.Length - 1];
                Type tt = cur.GetType();
                PropertyInfo pLast = tt.GetProperty(last, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (pLast != null && pLast.CanWrite)
                {
                    pLast.SetValue(cur, Coerce(pLast.PropertyType, value, cur as GameObject), null);
                    return;
                }
                FieldInfo fLast = tt.GetField(last, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (fLast != null)
                {
                    fLast.SetValue(cur, Coerce(fLast.FieldType, value, cur as GameObject));
                    return;
                }
                throw new Exception("member not settable: " + last);
            }

            static object Coerce(Type t, object v, GameObject context)
            {
                if (v is Dictionary<string, object> dd)
                {
                    if (dd.ContainsKey("x") || dd.ContainsKey("r")) v = DictToVec(t, dd);
                    else if (dd.ContainsKey("name")) v = dd["name"];
                    else if (dd.ContainsKey("id")) v = dd["id"];
                }
                if (t == typeof(GameObject)) { GameObject g = FindGameObject(v); return g; }
                if (typeof(Component).IsAssignableFrom(t))
                {
                    GameObject g = FindGameObject(v) ?? context;
                    return g == null ? null : g.GetComponent(t);
                }
                if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                {
                    string n = ToStr(v, null);
                    if (n == null) return null;
                    string[] guids = AssetDatabase.FindAssets(n + " t:" + t.Name);
                    foreach (string guid in guids)
                    {
                        string ap = AssetDatabase.GUIDToAssetPath(guid);
                        UnityEngine.Object o = AssetDatabase.LoadAssetAtPath(ap, t);
                        if (o != null) return o;
                    }
                    return null;
                }
                if (t == typeof(Vector3)) return ToVec3(v, Vector3.zero);
                if (t == typeof(Vector2)) return ToVec2(v, Vector2.zero);
                if (t == typeof(Quaternion))
                {
                    List<object> l = v as List<object>;
                    if (l != null && l.Count == 4) return new Quaternion(ToFloat(l[0], 0), ToFloat(l[1], 0), ToFloat(l[2], 0), ToFloat(l[3], 1));
                    if (l != null && l.Count == 3) return Quaternion.Euler(ToFloat(l[0], 0), ToFloat(l[1], 0), ToFloat(l[2], 0));
                    return Quaternion.identity;
                }
                if (t == typeof(Color)) return ToColor(v, Color.white);
                if (t == typeof(string)) return ToStr(v, "");
                if (t == typeof(bool)) return ToBool(v, false);
                if (t == typeof(int)) return (int)Math.Round(ToFloat(v, 0));
                if (t == typeof(long)) return (long)Math.Round(ToFloat(v, 0));
                if (t == typeof(float)) return ToFloat(v, 0f);
                if (t == typeof(double)) return ToFloat(v, 0);
                if (t.IsEnum)
                {
                    if (v is double dbl) return (int)dbl;
                    return Enum.Parse(t, ToStr(v, ""), true);
                }
                try { return Convert.ChangeType(v, t, CultureInfo.InvariantCulture); }
                catch { return null; }
            }

            static object DictToVec(Type t, Dictionary<string, object> d)
            {
                if (t == typeof(Vector3)) return new Vector3(ToFloat(d.ContainsKey("x") ? d["x"] : 0, 0), ToFloat(d.ContainsKey("y") ? d["y"] : 0, 0), ToFloat(d.ContainsKey("z") ? d["z"] : 0, 0));
                if (t == typeof(Vector2)) return new Vector2(ToFloat(d.ContainsKey("x") ? d["x"] : 0, 0), ToFloat(d.ContainsKey("y") ? d["y"] : 0, 0));
                if (t == typeof(Color)) return new Color(ToFloat(d.ContainsKey("r") ? d["r"] : 0, 0), ToFloat(d.ContainsKey("g") ? d["g"] : 0, 0), ToFloat(d.ContainsKey("b") ? d["b"] : 0, 0), ToFloat(d.ContainsKey("a") ? d["a"] : 1, 1));
                return null;
            }

            static object ToJsonValue(object v, GameObject context)
            {
                if (v == null) return null;
                Type t = v.GetType();
                if (t == typeof(Vector3)) { Vector3 vec = (Vector3)v; return Vec3Arr(vec); }
                if (t == typeof(Vector2)) { Vector2 vec = (Vector2)v; List<object> a = new List<object> { vec.x, vec.y }; return a; }
                if (t == typeof(Quaternion)) { Quaternion q = (Quaternion)v; List<object> a = new List<object> { q.x, q.y, q.z, q.w }; return a; }
                if (t == typeof(Color)) { Color c = (Color)v; List<object> a = new List<object> { c.r, c.g, c.b, c.a }; return a; }
                if (v is GameObject go) return GoRef(go);
                if (v is Component comp) return GoRef(comp.gameObject);
                if (t.IsPrimitive || v is string || v is decimal) return v;
                if (t.IsEnum) return v.ToString();
                if (v is IEnumerable && !(v is string))
                {
                    List<object> items = new List<object>();
                    int n = 0;
                    foreach (object item in (IEnumerable)v) { if (n++ >= 50) { items.Add("..."); break; } items.Add(ToJsonValue(item, context)); }
                    return items;
                }
                return v.ToString();
            }

            static List<object> Vec3Arr(Vector3 v) { List<object> a = new List<object>(); a.Add(v.x); a.Add(v.y); a.Add(v.z); return a; }

            // ---------- 参数转换 ----------
            public static object Get(Dictionary<string, object> p, string key) { object v; return p.TryGetValue(key, out v) ? v : null; }
            public static string ToStr(object v, string def) { return v == null ? def : v.ToString(); }
            public static bool ToBool(object v, bool def) { if (v is bool b) return b; if (v == null) return def; return v.ToString().ToLowerInvariant() == "true"; }
            public static int ToInt(object v, int def) { if (v == null) return def; return (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)); }
            public static float ToFloat(object v, float def) { if (v == null) return def; return (float)Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            public static Vector3 ToVec3(object v, Vector3 def)
            {
                List<object> l = v as List<object>;
                if (l == null || l.Count < 3) return def;
                return new Vector3(ToFloat(l[0], 0), ToFloat(l[1], 0), ToFloat(l[2], 0));
            }
            public static Vector2 ToVec2(object v, Vector2 def)
            {
                List<object> l = v as List<object>;
                if (l == null || l.Count < 2) return def;
                return new Vector2(ToFloat(l[0], 0), ToFloat(l[1], 0));
            }
            public static Color ToColor(object v, Color def)
            {
                List<object> l = v as List<object>;
                if (l == null || l.Count < 3) return def;
                return new Color(ToFloat(l[0], 0), ToFloat(l[1], 0), ToFloat(l[2], 0), l.Count >= 4 ? ToFloat(l[3], 1) : 1f);
            }
        }

        // ---------- 极简 JSON 解析/序列化 ----------

        public static class Json
        {
            public static object Parse(string s) { int i = 0; return ParseValue(s, ref i); }

            static object ParseValue(string s, ref int i)
            {
                SkipWs(s, ref i);
                char c = s[i];
                if (c == '{') return ParseObject(s, ref i);
                if (c == '[') return ParseArray(s, ref i);
                if (c == '"') return ParseString(s, ref i);
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return ParseNumber(s, ref i);
            }

            static void SkipWs(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }

            static Dictionary<string, object> ParseObject(string s, ref int i)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++; SkipWs(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs(s, ref i);
                    string key = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    i++; // ':'
                    object value = ParseValue(s, ref i);
                    d[key] = value;
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; // '}'
                    return d;
                }
            }

            static List<object> ParseArray(string s, ref int i)
            {
                List<object> l = new List<object>();
                i++; SkipWs(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; // ']'
                    return l;
                }
            }

            static string ParseString(string s, ref int i)
            {
                i++; // '"'
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    char c = s[i++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        char e = s[i++];
                        if (e == 'n') sb.Append('\n');
                        else if (e == 't') sb.Append('\t');
                        else if (e == 'r') sb.Append('\r');
                        else if (e == 'u')
                        {
                            string hex = s.Substring(i, 4);
                            i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                        }
                        else sb.Append(e);
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            static double ParseNumber(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            public static string Serialize(object v)
            {
                StringBuilder sb = new StringBuilder();
                Write(sb, v);
                return sb.ToString();
            }

            static void Write(StringBuilder sb, object v)
            {
                if (v == null) { sb.Append("null"); return; }
                if (v is string s) { WriteString(sb, s); return; }
                if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
                if (v is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (v is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (v is int i) { sb.Append(i); return; }
                if (v is long l) { sb.Append(l); return; }
                if (v is Dictionary<string, object> dict)
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                }
                if (v is IEnumerable en && !(v is string))
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in en) { if (!first) sb.Append(','); first = false; Write(sb, item); }
                    sb.Append(']');
                    return;
                }
                WriteString(sb, v.ToString());
            }

            static void WriteString(StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else if (c == '\n') sb.Append("\\n");
                    else if (c == '\r') sb.Append("\\r");
                    else if (c == '\t') sb.Append("\\t");
                    else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                sb.Append('"');
            }
        }
    }

    public class DshBridgeWindow : EditorWindow
    {
        Texture2D _preview;
        string _lastShot = "";
        double _lastRepaint;

        [MenuItem("DshBridge/Status Window")]
        public static void ShowWindow()
        {
            GetWindow<DshBridgeWindow>("DSH 桥接状态");
        }

        void OnEnable() { EditorApplication.update += RepaintTick; }
        void OnDisable() { EditorApplication.update -= RepaintTick; }

        void RepaintTick()
        {
            if (EditorApplication.timeSinceStartup - _lastRepaint > 1.0)
            {
                _lastRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        void OnGUI()
        {
            GUILayout.Space(6);
            bool running = DshBridgeServer.IsRunning;
            EditorGUILayout.BeginHorizontal();
            GUI.color = running ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.95f, 0.3f, 0.3f);
            GUILayout.Label("●", GUILayout.Width(18));
            GUI.color = Color.white;
            EditorGUILayout.LabelField("桥接服务", running
                ? ("运行中 · http://127.0.0.1:" + DshBridgeServer.Port + "  ·  " + DshBridgeServer.ProjectId)
                : "未启动");
            if (GUILayout.Button("重启服务", GUILayout.Width(90))) DshBridgeServer.Restart();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            DrawState("编译中", EditorApplication.isCompiling);
            DrawState("播放中", EditorApplication.isPlaying);
            DrawState("域重载", EditorApplication.isPlayingOrWillChangePlaymode);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            string task = DshBridgeServer.CurrentTask;
            EditorGUILayout.LabelField("当前任务", string.IsNullOrEmpty(task) ? "空闲" : task);

            GUILayout.Space(6);
            EditorGUILayout.LabelField("最新截图", EditorStyles.boldLabel);
            string shot = DshBridgeServer.LatestScreenshotPath;
            if (!string.IsNullOrEmpty(shot) && shot != _lastShot)
            {
                _lastShot = shot;
                try
                {
                    byte[] data = File.ReadAllBytes(shot);
                    if (_preview == null) _preview = new Texture2D(2, 2);
                    _preview.LoadImage(data);
                }
                catch { }
            }
            if (_preview != null)
            {
                float avail = position.width - 24;
                float scale = Mathf.Min(1f, avail / Mathf.Max(1, _preview.width));
                GUILayout.Box(_preview, GUILayout.Width(Mathf.Max(1, _preview.width * scale)), GUILayout.Height(Mathf.Max(1, _preview.height * scale)));
            }
            else
            {
                EditorGUILayout.HelpBox("还没有截图。运行演示或调用 screenshot 命令后这里会显示画面。", MessageType.Info);
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Console 日志", EditorStyles.boldLabel);
            foreach (DshBridgeServer.LogInfo log in DshBridgeServer.GetRecentLogs(20))
            {
                Color c = log.Type == "Error" ? new Color(1f, 0.55f, 0.55f) : log.Type == "Warning" ? new Color(1f, 0.82f, 0.5f) : new Color(0.85f, 0.87f, 0.9f);
                GUI.color = c;
                EditorGUILayout.LabelField(log.Message, EditorStyles.wordWrappedLabel);
            }
            GUI.color = Color.white;
        }

        void DrawState(string label, bool on)
        {
            Color prev = GUI.color;
            GUI.color = on ? new Color(1f, 0.75f, 0.2f) : new Color(0.4f, 0.4f, 0.4f);
            EditorGUILayout.LabelField(label, on ? "忙" : "空闲", GUILayout.Width(70));
            GUI.color = prev;
        }
    }
}
