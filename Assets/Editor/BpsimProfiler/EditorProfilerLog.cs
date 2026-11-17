#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Bpsim.Perf
{
    internal sealed class EditorProfilerLog : IDisposable
    {
        internal const int FileBytes = 4 * 1024 * 1024;
        internal const int FileCount = 4;
        const int QueueCapacity = 32;
        static readonly Regex LogName = new Regex(@"^editor-[A-Za-z0-9_-]{1,96}-\d{6}\.jsonl$");
        readonly BlockingCollection<string> queue = new BlockingCollection<string>(QueueCapacity);
        readonly Thread worker;
        readonly string directory, session, header;
        readonly int maxBytes, maxFiles;
        readonly UTF8Encoding encoding = new UTF8Encoding(false);
        string currentFile, error;
        long writtenLines, droppedLines;
        int stopped;

        internal EditorProfilerLog(string directory, string session, string header,
            int maxBytes = FileBytes, int maxFiles = FileCount)
        {
            if (maxBytes < 256 || maxFiles < 1) throw new ArgumentOutOfRangeException();
            this.directory = Path.GetFullPath(directory);
            this.session = session;
            this.header = header;
            this.maxBytes = maxBytes;
            this.maxFiles = maxFiles;
            worker = new Thread(WriteLoop) { IsBackground = true, Name = "Bpsim Editor Profiler Log" };
            worker.Start();
        }

        internal bool Enqueue(string json)
        {
            if (Volatile.Read(ref stopped) != 0) return false;
            try
            {
                if (queue.TryAdd(json)) return true;
            }
            catch (InvalidOperationException) { }
            Interlocked.Increment(ref droppedLines);
            return false;
        }

        internal Dictionary<string, object> Status()
        {
            return new Dictionary<string, object> { { "directory", directory },
                { "currentFile", Volatile.Read(ref currentFile) }, { "maxFileBytes", maxBytes },
                { "maxFiles", maxFiles }, { "queueCapacity", QueueCapacity },
                { "queuedLines", Volatile.Read(ref stopped) == 0 ? queue.Count : 0 },
                { "writtenLines", Interlocked.Read(ref writtenLines) },
                { "droppedLines", Interlocked.Read(ref droppedLines) },
                { "error", Volatile.Read(ref error) } };
        }

        void WriteLoop()
        {
            StreamWriter output = null;
            int segment = 0;
            try
            {
                foreach (string line in queue.GetConsumingEnumerable())
                {
                    try
                    {
                        int bytes = encoding.GetByteCount(line) + 1;
                        if (bytes + encoding.GetByteCount(header) + 1 > maxBytes)
                            throw new IOException("Profiler sample exceeds the log file size limit");
                        if (output == null || output.BaseStream.Position + bytes > maxBytes)
                        {
                            output?.Dispose();
                            output = null;
                            Directory.CreateDirectory(directory);
                            string path = Path.Combine(directory, "editor-" + session + "-" + (segment++).ToString("D6") + ".jsonl");
                            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                FileShare.ReadWrite | FileShare.Delete);
                            output = new StreamWriter(stream, encoding) { NewLine = "\n" };
                            output.WriteLine(header);
                            output.Flush();
                            Volatile.Write(ref currentFile, path);
                            Prune(path);
                        }
                        output.WriteLine(line);
                        output.Flush(); // One summary per second, never on the Editor thread.
                        Interlocked.Increment(ref writtenLines);
                        Volatile.Write(ref error, null);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref droppedLines);
                        Volatile.Write(ref error, exception.GetType().Name + ": " + exception.Message);
                        try { output?.Dispose(); } catch { }
                        output = null;
                    }
                }
            }
            catch (Exception exception) { Volatile.Write(ref error, exception.Message); }
            finally { try { output?.Dispose(); } catch { } }
        }

        void Prune(string keep)
        {
            var files = new List<FileInfo>();
            foreach (FileInfo file in new DirectoryInfo(directory).GetFiles("editor-*.jsonl"))
                if (LogName.IsMatch(file.Name) && (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    files.Add(file);
            files.Sort((a, b) =>
            {
                int time = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                return time != 0 ? time : string.CompareOrdinal(a.Name, b.Name);
            });
            int remaining = files.Count;
            foreach (FileInfo file in files)
            {
                if (remaining <= maxFiles) break;
                if (string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                // Only this collector's fixed filename pattern below its own directory is pruned.
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                file.Delete();
                remaining--;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            queue.CompleteAdding();
            if (worker.Join(1500)) queue.Dispose();
            else Volatile.Write(ref error, "Log worker did not finish before the reload shutdown deadline");
        }
    }
}
#endif
