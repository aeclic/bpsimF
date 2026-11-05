using System;
using System.Collections.Generic;
using UniWebViewExternal;

/// <summary>
/// Strongly-typed metadata parsed from native Safe Browsing callbacks.
/// </summary>
[Serializable]
public class UniWebViewSafeBrowsingEventMetadata {
    /// <summary>
    /// Discrete event kinds reported by native Safe Browsing implementations.
    /// </summary>
    public enum EventKind {
        Unknown,
        NavigationStarted,
        NavigationFinished,
        NavigationFailed,
        Minimized,
        Unminimized,
        WarmupComplete,
        TabHidden
    }

    /// <summary>
    /// Event type as indicated by the native payload.
    /// </summary>
    public EventKind Kind { get; private set; }

    /// <summary>
    /// Native source identifier (e.g. CustomTabsCallback or SFSafariViewController).
    /// </summary>
    public string Source { get; private set; }

    /// <summary>
    /// Epoch timestamp in milliseconds, if provided by the native payload.
    /// </summary>
    public long? Timestamp { get; private set; }

    /// <summary>
    /// Parsed UTC timestamp derived from the epoch value.
    /// </summary>
    public DateTimeOffset? TimestampUtc {
        get {
            if (!Timestamp.HasValue) {
                return null;
            }
            try {
                return DateTimeOffset.FromUnixTimeMilliseconds(Timestamp.Value);
            } catch {
                return null;
            }
        }
    }

    /// <summary>
    /// Convenience accessor for the local time representation of <see cref="TimestampUtc"/>.
    /// </summary>
    public DateTime? TimestampLocal {
        get {
            var utc = TimestampUtc;
            if (!utc.HasValue) {
                return null;
            }
            return utc.Value.LocalDateTime;
        }
    }

    /// <summary>
    /// Indicates whether parsing succeeded with a valid JSON payload.
    /// </summary>
    public bool IsValid { get; private set; }

    /// <summary>
    /// Raw JSON string received from native side. Useful for debugging.
    /// </summary>
    public string Raw { get; private set; }

    private UniWebViewSafeBrowsingEventMetadata() {
        Source = string.Empty;
        Raw = string.Empty;
        Kind = EventKind.Unknown;
    }

    internal static UniWebViewSafeBrowsingEventMetadata FromRaw(string raw, EventKind fallbackKind) {
        var metadata = new UniWebViewSafeBrowsingEventMetadata();
        metadata.Kind = fallbackKind;
        metadata.Raw = raw ?? string.Empty;
        metadata.IsValid = false;

        if (string.IsNullOrEmpty(raw)) {
            return metadata;
        }

        try {
            var parsed = Json.Deserialize(raw) as Dictionary<string, object>;
            if (parsed == null) {
                return metadata;
            }

            metadata.Source = ReadString(parsed, "source");
            metadata.Timestamp = ReadLong(parsed, "timestamp");

            var eventName = ReadString(parsed, "event");
            metadata.Kind = ParseKind(eventName, fallbackKind);
            metadata.IsValid = true;
        } catch (Exception e) {
            UniWebViewLogger.Instance.Debug("Failed to parse Safe Browsing metadata: " + e.Message);
        }

        return metadata;
    }

    private static EventKind ParseKind(string value, EventKind fallback) {
        if (string.IsNullOrEmpty(value)) {
            return fallback;
        }

        switch (value) {
            case "warmup_complete":
                return EventKind.WarmupComplete;
            case "minimize":
                return EventKind.Minimized;
            case "unminimize":
                return EventKind.Unminimized;
            case "navigation_started":
                return EventKind.NavigationStarted;
            case "navigation_finished":
                return EventKind.NavigationFinished;
            case "navigation_failed":
                return EventKind.NavigationFailed;
            case "tab_hidden":
                return EventKind.TabHidden;
            default:
                return fallback;
        }
    }

    private static string ReadString(Dictionary<string, object> dict, string key) {
        if (!dict.ContainsKey(key) || dict[key] == null) {
            return string.Empty;
        }
        return dict[key].ToString();
    }

    private static long? ReadLong(Dictionary<string, object> dict, string key) {
        if (!dict.ContainsKey(key) || dict[key] == null) {
            return null;
        }

        var value = dict[key];
        if (value is long longValue) {
            return longValue;
        }
        if (value is double doubleValue) {
            return Convert.ToInt64(doubleValue);
        }
        if (long.TryParse(value.ToString(), out var parsed)) {
            return parsed;
        }
        return null;
    }
}
