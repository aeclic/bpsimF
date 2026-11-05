using System;
using System.Collections.Generic;
using System.Globalization;
using UniWebViewExternal;
using UnityEngine;

/// <summary>
/// Represents the configuration for the embedded toolbar.
///
/// A config object describes the full toolbar layout: which items appear (back, forward, done, reload, title, or
/// custom buttons), how they are arranged (left, center, right sections), their colors, and title interaction
/// behaviors.
///
/// Use `UniWebViewEmbeddedToolbarConfig.Default` for the standard layout (back + forward on the left, title in the
/// center, done on the right), or construct your own config from scratch. Apply the config by calling
/// `UniWebViewEmbeddedToolbar.ApplyConfig`.
/// </summary>
[Serializable]
public class UniWebViewEmbeddedToolbarConfig {
    /// <summary>
    /// The current config version. Currently `1`.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The prefix used for all built-in item identifiers.
    /// </summary>
    public const string BuiltInIdentifierPrefix = "uwv.toolbar.";

    /// <summary>
    /// Contains the identifier constants for built-in toolbar items.
    /// </summary>
    public static class BuiltInIdentifier {
        /// <summary>Identifier for the back navigation button.</summary>
        public const string Back = "uwv.toolbar.back";
        /// <summary>Identifier for the forward navigation button.</summary>
        public const string Forward = "uwv.toolbar.forward";
        /// <summary>Identifier for the done (close) button.</summary>
        public const string Done = "uwv.toolbar.done";
        /// <summary>Identifier for the reload button.</summary>
        public const string Reload = "uwv.toolbar.reload";
        /// <summary>Identifier for the title item.</summary>
        public const string Title = "uwv.toolbar.title";
    }

    /// <summary>
    /// The type of a toolbar item.
    /// </summary>
    public enum ItemType {
        /// <summary>A built-in item with predefined behavior (back, forward, done, reload, title).</summary>
        BuiltIn,
        /// <summary>A custom button defined by the user.</summary>
        Custom
    }

    /// <summary>
    /// The kind of a built-in toolbar item.
    /// </summary>
    public enum BuiltInItemKind {
        /// <summary>Back navigation button. Default text: "❮".</summary>
        Back,
        /// <summary>Forward navigation button. Default text: "❯".</summary>
        Forward,
        /// <summary>Done (close) button. Default text: "Done".</summary>
        Done,
        /// <summary>Reload button. Default text: "↻".</summary>
        Reload,
        /// <summary>Title label displayed in the center section.</summary>
        Title
    }

    /// <summary>
    /// Represents a color with RGBA components, each in the range [0, 1].
    /// </summary>
    [Serializable]
    public class ColorValue {
        /// <summary>Red component, in the range [0, 1].</summary>
        public float r;
        /// <summary>Green component, in the range [0, 1].</summary>
        public float g;
        /// <summary>Blue component, in the range [0, 1].</summary>
        public float b;
        /// <summary>Alpha component, in the range [0, 1].</summary>
        public float a;

        /// <summary>
        /// Creates a `ColorValue` from a Unity `Color`.
        /// </summary>
        /// <param name="color">The Unity color to convert.</param>
        /// <returns>A new `ColorValue` instance.</returns>
        public static ColorValue FromColor(Color color) {
            return new ColorValue {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a
            };
        }

        /// <summary>
        /// Converts this `ColorValue` to a Unity `Color`.
        /// </summary>
        /// <returns>A Unity `Color` instance.</returns>
        public Color ToColor() {
            return new Color(r, g, b, a);
        }
    }

    /// <summary>
    /// Represents the visual style of a toolbar item.
    /// </summary>
    [Serializable]
    public class ItemStyle {
        /// <summary>
        /// The text color of this item. When set, overrides the global `ButtonTextColor` or `TitleTextColor`.
        /// </summary>
        public ColorValue TextColor;
    }

    /// <summary>
    /// Configures interaction behaviors for the built-in Title item.
    ///
    /// You can set `OnTap` to `ScrollToTop` so tapping the title scrolls the web view to the top.
    /// You can set `OnLongPress` to `CopyUrl` so long-pressing the title copies the current URL to the clipboard
    /// and shows a toast message. The toast text can be customized via `CopyToastText`.
    /// </summary>
    [Serializable]
    public class TitleInteraction {
        /// <summary>
        /// The action to perform when the title item is tapped.
        /// </summary>
        public enum TapAction {
            /// <summary>No action on tap.</summary>
            None,
            /// <summary>Scroll the web view content to the top of the page.</summary>
            ScrollToTop
        }

        /// <summary>
        /// The action to perform when the title item is long-pressed.
        /// </summary>
        public enum LongPressAction {
            /// <summary>No action on long press.</summary>
            None,
            /// <summary>Copy the current page URL to the system clipboard and show a toast.</summary>
            CopyUrl
        }

        /// <summary>
        /// The action to perform when the title is tapped. Default is `None`.
        /// </summary>
        public TapAction OnTap = TapAction.None;

        /// <summary>
        /// The action to perform when the title is long-pressed. Default is `None`.
        /// </summary>
        public LongPressAction OnLongPress = LongPressAction.None;

        /// <summary>
        /// The toast message text shown after copying the URL. If not set, "URL copied" is used.
        /// Only applicable when `OnLongPress` is `CopyUrl`.
        /// </summary>
        public string CopyToastText;
    }

    /// <summary>
    /// Represents a single item in the toolbar. An item is either a built-in item (back, forward, done, reload,
    /// title) or a custom button. Use `Item.BuiltIn` or `Item.Custom` factory methods to create items.
    /// </summary>
    [Serializable]
    public class Item {
        /// <summary>The type of this item (built-in or custom).</summary>
        public ItemType Type;

        /// <summary>
        /// A unique identifier for this item. Built-in items use `uwv.toolbar.*` identifiers by default.
        /// Custom items must provide their own identifier.
        /// </summary>
        public string Identifier;

        /// <summary>Optional per-item visual style. When set, overrides global color settings.</summary>
        public ItemStyle Style;

        /// <summary>The kind of built-in item. Only applicable when `Type` is `BuiltIn`.</summary>
        public BuiltInItemKind? Kind;

        /// <summary>
        /// The display text for this item. For built-in items, if not set, a platform default is used.
        /// For custom items, this is the button label.
        /// </summary>
        public string Title;

        /// <summary>
        /// Whether this item is visible. If `null` or `true`, the item is shown. Set to `false` to hide it.
        /// </summary>
        public bool? Visible;

        /// <summary>
        /// Interaction config for the built-in Title item. Only applicable when `Kind` is `Title`.
        /// </summary>
        public TitleInteraction TitleInteraction;

        /// <summary>
        /// Creates a built-in toolbar item.
        /// </summary>
        /// <param name="kind">The kind of built-in item to create.</param>
        /// <param name="title">Optional title text override. If `null`, the platform default is used.</param>
        /// <param name="visible">Optional visibility. If `null`, the item is visible.</param>
        /// <param name="style">Optional per-item style override.</param>
        /// <param name="titleInteraction">Optional title interaction config. Only for `Title` items.</param>
        /// <param name="identifier">Optional identifier override. By default uses `uwv.toolbar.*`.</param>
        /// <returns>A new built-in `Item` instance.</returns>
        public static Item BuiltIn(
            BuiltInItemKind kind,
            string title = null,
            bool? visible = null,
            ItemStyle style = null,
            TitleInteraction titleInteraction = null,
            string identifier = null
        ) {
            return new Item {
                Type = ItemType.BuiltIn,
                Kind = kind,
                Identifier = string.IsNullOrEmpty(identifier) ? IdentifierForBuiltInKind(kind) : identifier,
                Title = title,
                Visible = visible,
                Style = style,
                TitleInteraction = titleInteraction
            };
        }

        /// <summary>
        /// Creates a custom toolbar button.
        /// </summary>
        /// <param name="identifier">A unique identifier for this item. Used to identify it in action callbacks.</param>
        /// <param name="title">The button label text.</param>
        /// <param name="style">Optional per-item style.</param>
        /// <returns>A new custom `Item` instance.</returns>
        public static Item Custom(string identifier, string title, ItemStyle style = null) {
            return new Item {
                Type = ItemType.Custom,
                Identifier = identifier,
                Title = title,
                Style = style
            };
        }

        /// <summary>
        /// Returns whether this item is visible. An item is visible when `Visible` is `null` or `true`.
        /// </summary>
        /// <returns>`true` if the item should be displayed.</returns>
        public bool IsVisible() {
            return !Visible.HasValue || Visible.Value;
        }
    }

    /// <summary>
    /// Gets the JSON string representation of the default config.
    /// </summary>
    public static string DefaultJson => CreateDefault().ToJson();

    /// <summary>
    /// Gets the default toolbar config.
    ///
    /// The default contains: back and forward buttons on the left, a title item in the center, and a done button
    /// on the right. Position is `Top`, visibility is `true`, and colors use platform defaults.
    /// </summary>
    public static UniWebViewEmbeddedToolbarConfig Default => CreateDefault();

    /// <summary>The config version. Currently always `1`.</summary>
    public int Version = CurrentVersion;

    /// <summary>Whether the toolbar is visible. Default is `true`.</summary>
    public bool Visible = true;

    /// <summary>
    /// The position of the toolbar (`Top` or `Bottom`). Default is `Top`.
    /// Only effective on iOS and Android; macOS toolbar is always in the title bar.
    /// </summary>
    public UniWebViewToolbarPosition Position = UniWebViewToolbarPosition.Top;

    /// <summary>
    /// The maximum height of the toolbar in points. If smaller than the standard height, the toolbar shrinks.
    /// Only effective on iOS and Android.
    /// </summary>
    public float? MaxHeight;

    /// <summary>The background color of the toolbar. When `null`, the platform default is used.</summary>
    public ColorValue BackgroundColor;

    /// <summary>
    /// The default text color for toolbar buttons. When `null`, the platform default is used.
    /// Individual items can override this via `Item.Style.TextColor`.
    /// </summary>
    public ColorValue ButtonTextColor;

    /// <summary>
    /// The text color for the title item. When `null`, the platform default is used.
    /// The title item can override this via its own `Item.Style.TextColor`.
    /// </summary>
    public ColorValue TitleTextColor;

    /// <summary>Items in the left section. Default: back and forward buttons.</summary>
    public List<Item> Left = new List<Item>();

    /// <summary>Items in the center section. Default: title item.</summary>
    public List<Item> Center = new List<Item>();

    /// <summary>Items in the right section. Default: done button.</summary>
    public List<Item> Right = new List<Item>();

    /// <summary>
    /// Serializes this config to a JSON string.
    /// </summary>
    /// <returns>A JSON string representing this config.</returns>
    public string ToJson() {
        return Json.Serialize(ToDictionary());
    }

    /// <summary>
    /// Converts this config to a dictionary representation.
    /// </summary>
    /// <returns>A dictionary that can be serialized to JSON.</returns>
    public Dictionary<string, object> ToDictionary() {
        var dict = new Dictionary<string, object> {
            { "version", Version },
            { "visible", Visible },
            { "position", SerializePosition(Position) },
            { "maxHeight", MaxHeight.HasValue ? (object)MaxHeight.Value : null },
            { "backgroundColor", SerializeColor(BackgroundColor) },
            { "buttonTextColor", SerializeColor(ButtonTextColor) },
            { "titleTextColor", SerializeColor(TitleTextColor) },
            { "left", SerializeItems(Left) },
            { "center", SerializeItems(Center) },
            { "right", SerializeItems(Right) }
        };

        return dict;
    }

    /// <summary>
    /// Creates a config from a JSON string.
    /// </summary>
    /// <param name="json">A JSON string representing the toolbar config.</param>
    /// <returns>A parsed config, or `null` if the JSON is invalid or empty.</returns>
    public static UniWebViewEmbeddedToolbarConfig FromJson(string json) {
        if (string.IsNullOrEmpty(json)) {
            return null;
        }

        try {
            var dict = Json.Deserialize(json) as Dictionary<string, object>;
            if (dict == null) {
                UniWebViewLogger.Instance.Debug("Embedded toolbar config JSON is not an object.");
                return null;
            }
            return FromDictionary(dict);
        } catch (Exception e) {
            UniWebViewLogger.Instance.Debug("Failed to parse embedded toolbar config JSON: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Creates a config from a dictionary.
    /// </summary>
    /// <param name="dict">A dictionary representing the toolbar config.</param>
    /// <returns>A parsed config, or `null` if the dictionary is `null`.</returns>
    public static UniWebViewEmbeddedToolbarConfig FromDictionary(Dictionary<string, object> dict) {
        if (dict == null) {
            return null;
        }

        var config = new UniWebViewEmbeddedToolbarConfig {
            Version = ReadInt(dict, "version", CurrentVersion),
            Visible = ReadBool(dict, "visible", true),
            Position = ParsePosition(ReadString(dict, "position")),
            MaxHeight = ReadNullableFloat(dict, "maxHeight"),
            BackgroundColor = ReadColor(dict, "backgroundColor"),
            ButtonTextColor = ReadColor(dict, "buttonTextColor"),
            TitleTextColor = ReadColor(dict, "titleTextColor")
        };

        config.Left = ReadItems(dict, "left");
        config.Center = ReadItems(dict, "center");
        config.Right = ReadItems(dict, "right");

        return config;
    }

    /// <summary>
    /// Enumerates all items across all sections (left, center, right) in order.
    /// </summary>
    /// <returns>An enumerable of all items.</returns>
    public IEnumerable<Item> EnumerateItems() {
        foreach (var item in Left) {
            yield return item;
        }

        foreach (var item in Center) {
            yield return item;
        }

        foreach (var item in Right) {
            yield return item;
        }
    }

    /// <summary>
    /// Finds the first built-in item matching the given kind across all sections (left, center, right).
    /// </summary>
    /// <param name="kind">The built-in item kind to search for.</param>
    /// <returns>The matching item, or `null` if not found.</returns>
    public Item FindFirstBuiltInItem(BuiltInItemKind kind) {
        foreach (var item in EnumerateItems()) {
            if (item == null || item.Type != ItemType.BuiltIn || !item.Kind.HasValue) {
                continue;
            }

            if (item.Kind.Value == kind) {
                return item;
            }
        }

        return null;
    }

    private static UniWebViewEmbeddedToolbarConfig CreateDefault() {
        return new UniWebViewEmbeddedToolbarConfig {
            Version = CurrentVersion,
            Visible = true,
            Position = UniWebViewToolbarPosition.Top,
            MaxHeight = null,
            BackgroundColor = null,
            ButtonTextColor = null,
            TitleTextColor = null,
            Left = new List<Item> {
                Item.BuiltIn(BuiltInItemKind.Back),
                Item.BuiltIn(BuiltInItemKind.Forward)
            },
            Center = new List<Item> {
                Item.BuiltIn(BuiltInItemKind.Title, title: string.Empty)
            },
            Right = new List<Item> {
                Item.BuiltIn(BuiltInItemKind.Done)
            }
        };
    }

    private static string IdentifierForBuiltInKind(BuiltInItemKind kind) {
        switch (kind) {
            case BuiltInItemKind.Back:
                return BuiltInIdentifier.Back;
            case BuiltInItemKind.Forward:
                return BuiltInIdentifier.Forward;
            case BuiltInItemKind.Done:
                return BuiltInIdentifier.Done;
            case BuiltInItemKind.Reload:
                return BuiltInIdentifier.Reload;
            case BuiltInItemKind.Title:
                return BuiltInIdentifier.Title;
            default:
                return string.Empty;
        }
    }

    private static string SerializePosition(UniWebViewToolbarPosition position) {
        return position == UniWebViewToolbarPosition.Bottom ? "bottom" : "top";
    }

    private static UniWebViewToolbarPosition ParsePosition(string value) {
        return string.Equals(value, "bottom", StringComparison.OrdinalIgnoreCase)
            ? UniWebViewToolbarPosition.Bottom
            : UniWebViewToolbarPosition.Top;
    }

    private static string SerializeItemType(ItemType type) {
        return type == ItemType.Custom ? "custom" : "builtIn";
    }

    private static ItemType ParseItemType(string type) {
        return string.Equals(type, "custom", StringComparison.OrdinalIgnoreCase)
            ? ItemType.Custom
            : ItemType.BuiltIn;
    }

    private static string SerializeBuiltInKind(BuiltInItemKind? kind) {
        if (!kind.HasValue) {
            return null;
        }

        switch (kind.Value) {
            case BuiltInItemKind.Back:
                return "back";
            case BuiltInItemKind.Forward:
                return "forward";
            case BuiltInItemKind.Done:
                return "done";
            case BuiltInItemKind.Reload:
                return "reload";
            case BuiltInItemKind.Title:
                return "title";
            default:
                return null;
        }
    }

    private static BuiltInItemKind? ParseBuiltInKind(string kind) {
        if (string.IsNullOrEmpty(kind)) {
            return null;
        }

        switch (kind.ToLowerInvariant()) {
            case "back":
                return BuiltInItemKind.Back;
            case "forward":
                return BuiltInItemKind.Forward;
            case "done":
                return BuiltInItemKind.Done;
            case "reload":
                return BuiltInItemKind.Reload;
            case "title":
                return BuiltInItemKind.Title;
            default:
                return null;
        }
    }

    private static object SerializeColor(ColorValue color) {
        if (color == null) {
            return null;
        }

        return new Dictionary<string, object> {
            { "r", color.r },
            { "g", color.g },
            { "b", color.b },
            { "a", color.a }
        };
    }

    private static ColorValue ReadColor(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return null;
        }

        if (!(raw is Dictionary<string, object> colorDict)) {
            return null;
        }

        if (!TryReadFloat(colorDict, "r", out var r) ||
            !TryReadFloat(colorDict, "g", out var g) ||
            !TryReadFloat(colorDict, "b", out var b) ||
            !TryReadFloat(colorDict, "a", out var a)) {
            return null;
        }

        return new ColorValue {
            r = r,
            g = g,
            b = b,
            a = a
        };
    }

    private static object SerializeStyle(ItemStyle style) {
        if (style == null) {
            return null;
        }

        return new Dictionary<string, object> {
            { "textColor", SerializeColor(style.TextColor) }
        };
    }

    private static ItemStyle ReadStyle(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return null;
        }

        if (!(raw is Dictionary<string, object> styleDict)) {
            return null;
        }

        return new ItemStyle {
            TextColor = ReadColor(styleDict, "textColor")
        };
    }

    private static object SerializeTitleInteraction(TitleInteraction interaction) {
        if (interaction == null) {
            return null;
        }

        return new Dictionary<string, object> {
            { "onTap", SerializeTitleTapAction(interaction.OnTap) },
            { "onLongPress", SerializeTitleLongPressAction(interaction.OnLongPress) },
            { "copyToastText", interaction.CopyToastText }
        };
    }

    private static TitleInteraction ReadTitleInteraction(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return null;
        }

        if (!(raw is Dictionary<string, object> interactionDict)) {
            return null;
        }

        return new TitleInteraction {
            OnTap = ParseTitleTapAction(ReadString(interactionDict, "onTap")),
            OnLongPress = ParseTitleLongPressAction(ReadString(interactionDict, "onLongPress")),
            CopyToastText = ReadString(interactionDict, "copyToastText")
        };
    }

    private static string SerializeTitleTapAction(TitleInteraction.TapAction action) {
        switch (action) {
            case TitleInteraction.TapAction.ScrollToTop:
                return "scrollToTop";
            default:
                return "none";
        }
    }

    private static TitleInteraction.TapAction ParseTitleTapAction(string value) {
        return string.Equals(value, "scrollToTop", StringComparison.OrdinalIgnoreCase)
            ? TitleInteraction.TapAction.ScrollToTop
            : TitleInteraction.TapAction.None;
    }

    private static string SerializeTitleLongPressAction(TitleInteraction.LongPressAction action) {
        switch (action) {
            case TitleInteraction.LongPressAction.CopyUrl:
                return "copyUrl";
            default:
                return "none";
        }
    }

    private static TitleInteraction.LongPressAction ParseTitleLongPressAction(string value) {
        return string.Equals(value, "copyUrl", StringComparison.OrdinalIgnoreCase)
            ? TitleInteraction.LongPressAction.CopyUrl
            : TitleInteraction.LongPressAction.None;
    }

    private static List<object> SerializeItems(List<Item> items) {
        var result = new List<object>();
        if (items == null) {
            return result;
        }

        foreach (var item in items) {
            if (item == null) {
                continue;
            }
            result.Add(SerializeItem(item));
        }

        return result;
    }

    private static object SerializeItem(Item item) {
        var dict = new Dictionary<string, object> {
            { "type", SerializeItemType(item.Type) },
            { "identifier", item.Identifier },
            { "style", SerializeStyle(item.Style) }
        };

        if (item.Type == ItemType.BuiltIn) {
            dict["kind"] = SerializeBuiltInKind(item.Kind);
            dict["title"] = item.Title;
            dict["visible"] = item.Visible.HasValue ? (object)item.Visible.Value : null;
            if (item.Kind == BuiltInItemKind.Title) {
                dict["titleInteraction"] = SerializeTitleInteraction(item.TitleInteraction);
            }
        } else {
            dict["title"] = item.Title;
        }

        return dict;
    }

    private static List<Item> ReadItems(Dictionary<string, object> dict, string key) {
        var items = new List<Item>();

        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return items;
        }

        if (!(raw is List<object> itemList)) {
            return items;
        }

        foreach (var itemObj in itemList) {
            if (!(itemObj is Dictionary<string, object> itemDict)) {
                continue;
            }

            var item = ReadItem(itemDict);
            if (item != null) {
                items.Add(item);
            }
        }

        return items;
    }

    private static Item ReadItem(Dictionary<string, object> dict) {
        var type = ParseItemType(ReadString(dict, "type"));
        var kind = ParseBuiltInKind(ReadString(dict, "kind"));
        var identifier = ReadString(dict, "identifier");
        if (string.IsNullOrEmpty(identifier) && type == ItemType.BuiltIn && kind.HasValue) {
            identifier = IdentifierForBuiltInKind(kind.Value);
        }
        if (type == ItemType.BuiltIn && !kind.HasValue && !string.IsNullOrEmpty(identifier)) {
            kind = ParseBuiltInKindFromIdentifier(identifier);
        }

        if (string.IsNullOrEmpty(identifier)) {
            UniWebViewLogger.Instance.Debug("Embedded toolbar config item misses identifier and will be ignored.");
            return null;
        }

        var item = new Item {
            Type = type,
            Identifier = identifier,
            Kind = type == ItemType.BuiltIn ? kind : null,
            Title = ReadString(dict, "title"),
            Visible = type == ItemType.BuiltIn ? ReadNullableBool(dict, "visible") : null,
            Style = ReadStyle(dict, "style")
        };

        if (type == ItemType.BuiltIn && item.Kind == BuiltInItemKind.Title) {
            item.TitleInteraction = ReadTitleInteraction(dict, "titleInteraction");
        }

        return item;
    }

    private static BuiltInItemKind? ParseBuiltInKindFromIdentifier(string identifier) {
        switch (identifier) {
            case BuiltInIdentifier.Back:
                return BuiltInItemKind.Back;
            case BuiltInIdentifier.Forward:
                return BuiltInItemKind.Forward;
            case BuiltInIdentifier.Done:
                return BuiltInItemKind.Done;
            case BuiltInIdentifier.Reload:
                return BuiltInItemKind.Reload;
            case BuiltInIdentifier.Title:
                return BuiltInItemKind.Title;
            default:
                return null;
        }
    }

    private static string ReadString(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var value) || value == null) {
            return null;
        }

        return value as string ?? value.ToString();
    }

    private static int ReadInt(Dictionary<string, object> dict, string key, int defaultValue) {
        if (!dict.TryGetValue(key, out var value) || value == null) {
            return defaultValue;
        }

        if (value is long longValue) {
            return (int)longValue;
        }

        if (value is double doubleValue) {
            return (int)doubleValue;
        }

        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static bool ReadBool(Dictionary<string, object> dict, string key, bool defaultValue) {
        var value = ReadNullableBool(dict, key);
        return value.HasValue ? value.Value : defaultValue;
    }

    private static bool? ReadNullableBool(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var value) || value == null) {
            return null;
        }

        if (value is bool boolValue) {
            return boolValue;
        }

        if (value is long longValue) {
            return longValue != 0;
        }

        if (value is double doubleValue) {
            return Math.Abs(doubleValue) > double.Epsilon;
        }

        if (bool.TryParse(value.ToString(), out var parsedBool)) {
            return parsedBool;
        }

        return null;
    }

    private static bool TryReadFloat(Dictionary<string, object> dict, string key, out float value) {
        value = 0f;
        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return false;
        }

        return TryConvertToFloat(raw, out value);
    }

    private static float? ReadNullableFloat(Dictionary<string, object> dict, string key) {
        if (!dict.TryGetValue(key, out var raw) || raw == null) {
            return null;
        }

        return TryConvertToFloat(raw, out var value) ? value : (float?)null;
    }

    private static bool TryConvertToFloat(object raw, out float value) {
        value = 0f;
        if (raw is float floatValue) {
            value = floatValue;
            return true;
        }

        if (raw is double doubleValue) {
            value = (float)doubleValue;
            return true;
        }

        if (raw is long longValue) {
            value = longValue;
            return true;
        }

        if (raw is int intValue) {
            value = intValue;
            return true;
        }

        return float.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>
/// Applies embedded toolbar config via JSON bridge.
/// </summary>
public static class UniWebViewEmbeddedToolbarConfigApplier {
    public interface IBridge {
        bool TrySetEmbeddedToolbarConfig(string name, string json);
    }

    private sealed class NativeBridge : IBridge {
        public bool TrySetEmbeddedToolbarConfig(string name, string json) {
            return UniWebViewInterface.SetEmbeddedToolbarConfig(name, json);
        }
    }

    private static readonly IBridge DefaultBridge = new NativeBridge();

    public static bool Apply(string name, UniWebViewEmbeddedToolbarConfig config) {
        return Apply(name, config, DefaultBridge);
    }

    public static bool ApplyJson(string name, string json) {
        return ApplyJson(name, json, DefaultBridge);
    }

    public static bool Apply(string name, UniWebViewEmbeddedToolbarConfig config, IBridge bridge) {
        if (string.IsNullOrEmpty(name)) {
            UniWebViewLogger.Instance.Debug("Embedded toolbar config apply skipped due to empty listener name.");
            return false;
        }

        if (config == null) {
            config = UniWebViewEmbeddedToolbarConfig.Default;
        }

        if (bridge == null) {
            bridge = DefaultBridge;
        }

        return ApplyJson(name, config.ToJson(), bridge);
    }

    public static bool ApplyJson(string name, string json, IBridge bridge) {
        if (string.IsNullOrEmpty(name)) {
            UniWebViewLogger.Instance.Debug("Embedded toolbar config apply skipped due to empty listener name.");
            return false;
        }

        if (string.IsNullOrEmpty(json)) {
            json = UniWebViewEmbeddedToolbarConfig.DefaultJson;
        }

        if (bridge == null) {
            bridge = DefaultBridge;
        }

        return bridge.TrySetEmbeddedToolbarConfig(name, json);
    }
}
