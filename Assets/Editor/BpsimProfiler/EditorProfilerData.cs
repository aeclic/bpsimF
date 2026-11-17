#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Bpsim.Perf
{
    internal static class EditorProfilerData
    {
        internal const int HistoryCapacity = 120;

        internal static Dictionary<string, object> Distribution(double[] values, int count)
        {
            if (count == 0)
                return new Dictionary<string, object> { { "sampleCount", 0 }, { "mean", null },
                    { "p50", null }, { "p95", null }, { "p99", null }, { "max", null }, { "sum", null } };
            double sum = 0;
            for (int i = 0; i < count; i++) sum += values[i];
            Array.Sort(values, 0, count);
            return new Dictionary<string, object> { { "sampleCount", count }, { "mean", sum / count },
                { "p50", values[(int)Math.Ceiling(count * .50) - 1] },
                { "p95", values[(int)Math.Ceiling(count * .95) - 1] },
                { "p99", values[(int)Math.Ceiling(count * .99) - 1] },
                { "max", values[count - 1] }, { "sum", sum } };
        }

        internal static void ValidateQuery(Dictionary<string, object> query,
            out int count, out long after, out string session)
        {
            query = query ?? new Dictionary<string, object>();
            foreach (string key in query.Keys)
                if (key != "count" && key != "afterSequence" && key != "sessionId")
                    throw new ArgumentException("Unknown profiler query parameter: " + key);
            count = (int)Integer(query, "count", 10, 1, HistoryCapacity);
            after = Integer(query, "afterSequence", 0, 0, 9007199254740991L);
            session = "";
            if (query.TryGetValue("sessionId", out object value))
            {
                session = value as string;
                if (session == null || session.Length > 96)
                    throw new ArgumentException("sessionId must be a string of at most 96 characters");
                foreach (char c in session)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                        !(c >= '0' && c <= '9') && c != '-' && c != '_')
                        throw new ArgumentException("Invalid profiler sessionId");
            }
        }

        static long Integer(Dictionary<string, object> query, string key, long fallback, long min, long max)
        {
            if (!query.TryGetValue(key, out object value)) return fallback;
            if (!(value is int) && !(value is long) && !(value is double))
                throw new ArgumentException(key + " must be an integer");
            double number = Convert.ToDouble(value);
            if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number) ||
                number < min || number > max)
                throw new ArgumentException(key + " is outside its permitted integer range");
            return (long)number;
        }
    }

    internal sealed class EditorProfilerHistory
    {
        readonly string session;
        readonly int capacity;
        readonly Queue<Dictionary<string, object>> samples = new Queue<Dictionary<string, object>>();
        internal long LatestSequence { get; private set; }
        internal Dictionary<string, object> Latest { get; private set; }

        internal EditorProfilerHistory(string session, int capacity = EditorProfilerData.HistoryCapacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.session = session;
            this.capacity = capacity;
        }

        internal void Add(Dictionary<string, object> sample)
        {
            sample["sessionId"] = session;
            sample["sequence"] = ++LatestSequence;
            samples.Enqueue(sample);
            if (samples.Count > capacity) samples.Dequeue();
            Latest = sample;
        }

        internal Dictionary<string, object> Read(int count, long after, string requestedSession)
        {
            bool reset = (!string.IsNullOrEmpty(requestedSession) && requestedSession != session) ||
                after > LatestSequence;
            long effectiveAfter = reset ? 0 : after;
            var eligible = new List<Dictionary<string, object>>();
            foreach (var sample in samples)
                if (Convert.ToInt64(sample["sequence"]) > effectiveAfter) eligible.Add(sample);
            int skip = effectiveAfter == 0 ? Math.Max(0, eligible.Count - count) : 0;
            var selected = eligible.GetRange(skip, Math.Min(count, eligible.Count - skip));
            long oldest = samples.Count == 0 ? 0 : Convert.ToInt64(samples.Peek()["sequence"]);
            long next = selected.Count == 0 ? LatestSequence : Convert.ToInt64(selected[selected.Count - 1]["sequence"]);
            return new Dictionary<string, object> { { "schema", 1 }, { "sessionId", session },
                { "latest", Latest }, { "samples", selected }, { "latestSequence", LatestSequence },
                { "oldestSequence", oldest }, { "nextSequence", next }, { "cursorReset", reset },
                { "historyTruncated", effectiveAfter > 0 && oldest > effectiveAfter + 1 },
                { "hasMore", skip + selected.Count < eligible.Count } };
        }
    }
}
#endif
