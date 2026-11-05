using UnityEngine;

/// <summary>
/// Represents the embedded toolbar in a web view.
///
/// You do not create an instance of this class directly. Instead, use the `EmbeddedToolbar` property in `UniWebView` to
/// get the current embedded toolbar in the web view and interact with it.
///
/// The embedded toolbar of a web view expands its width to match the web view's frame width. It is displayed at either
/// top or bottom of the web view, based on the setting received through `SetPosition`. By default, the toolbar contains
/// a main title, a back button, a forward button and a done button to close the web view.
///
/// You can customize the toolbar through the config-based approach: create a `UniWebViewEmbeddedToolbarConfig`, set up
/// items (built-in or custom), colors, title interactions, and apply it via `ApplyConfig`. This gives you full control
/// over the toolbar layout, including adding custom buttons, a reload button, and title interactions like scroll-to-top
/// or copy-URL-on-long-press.
///
/// For simple customization, convenience methods like `SetDoneButtonText`, `SetBackgroundColor`, etc. are also
/// available. These methods modify the current config internally and apply the changes automatically.
/// </summary>
public class UniWebViewEmbeddedToolbar {

    private readonly UniWebViewNativeListener listener;
    private UniWebViewEmbeddedToolbarConfig currentConfig;
    private string lastAppliedJson;

    /// <summary>
    /// Gets a cloned copy of the current toolbar config.
    ///
    /// You can use this to inspect the current state of the toolbar, or as a starting point for building a modified
    /// config. Changes to the returned object will not affect the toolbar until you pass it to `ApplyConfig`.
    /// </summary>
    public UniWebViewEmbeddedToolbarConfig CurrentConfig => CloneConfig(currentConfig);
    
    internal UniWebViewEmbeddedToolbar(UniWebViewNativeListener listener) {
        this.listener = listener;
        currentConfig = UniWebViewEmbeddedToolbarConfig.Default;
        lastAppliedJson = null;
    }
    
    /// <summary>
    /// Sets the position of the embedded toolbar. You can put the toolbar at either top or bottom of your web view.
    /// The default position is `Top`.
    /// </summary>
    /// <param name="position">The desired position of the toolbar.</param>
    public void SetPosition(UniWebViewToolbarPosition position) {
        currentConfig.Position = position;
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Shows the toolbar.
    /// </summary>
    public void Show() {
        currentConfig.Visible = true;
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Hides the toolbar.
    /// </summary>
    public void Hide() {
        currentConfig.Visible = false;
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Sets the text of the done button. The default text is "Done".
    /// </summary>
    /// <param name="text">The desired text to display as the done button.</param>
    public void SetDoneButtonText(string text) {
        SetBuiltInTitle(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Done, text);
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Sets the text of the back button. The default text is "❮" ("\u276E").
    /// </summary>
    /// <param name="text">The desired text to display as the back button.</param>
    public void SetGoBackButtonText(string text) {
        SetBuiltInTitle(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Back, text);
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Sets the text of the forward button. The default text is "❯" ("\u276F").
    /// </summary>
    /// <param name="text">The desired text to display as the forward button.</param>
    public void SetGoForwardButtonText(string text) {
        SetBuiltInTitle(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Forward, text);
        ApplyCurrentConfig();
    }
    
    /// <summary>
    /// Sets the text of toolbar title. The default is empty. The space is limited, setting a long text as title might
    /// not fit in the space. 
    /// </summary>
    /// <param name="text">The desired text to display as the title in the toolbar.</param>
    public void SetTitleText(string text) {
        SetBuiltInTitle(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Title, text);
        ApplyCurrentConfig();
    }
    
    /// <summary>
    /// Sets the background color of the toolbar.
    /// </summary>
    /// <param name="color">The desired color of toolbar's background.</param>
    public void SetBackgroundColor(Color color) {
        currentConfig.BackgroundColor = UniWebViewEmbeddedToolbarConfig.ColorValue.FromColor(color);
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Sets the buttons color of the toolbar. This color affects the back, forward and done button.
    /// </summary>
    /// <param name="color">The desired color of toolbar's buttons.</param>
    public void SetButtonTextColor(Color color) {
        currentConfig.ButtonTextColor = UniWebViewEmbeddedToolbarConfig.ColorValue.FromColor(color);
        ApplyCurrentConfig();
    }
    
    /// <summary>
    /// Sets the text color of the toolbar title. 
    /// </summary>
    /// <param name="color">The desired color of the toolbar's title.</param>
    public void SetTitleTextColor(Color color) {
        currentConfig.TitleTextColor = UniWebViewEmbeddedToolbarConfig.ColorValue.FromColor(color);
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Hides the navigation buttons on the toolbar. 
    /// 
    /// When called, the back button and forward button will not be shown.
    /// By default, the navigation buttons are shown.
    /// </summary>
    public void HideNavigationButtons() {
        SetBuiltInVisible(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Back, false);
        SetBuiltInVisible(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Forward, false);
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Shows the navigation buttons on the toolbar. 
    /// 
    /// When called, the back button and forward button will be shown.
    /// By default, the navigation buttons are shown.
    /// </summary>
    public void ShowNavigationButtons() {
        SetBuiltInVisible(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Back, true);
        SetBuiltInVisible(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind.Forward, true);
        ApplyCurrentConfig();
    }
    
    /// <summary>
    /// Sets the maximum height of the toolbar. If the specified height is smaller than the toolbar's standard height,
    /// the toolbar will be resized to this height. Otherwise, the standard height will be used.
    ///
    /// This method only works on iOS and Android. On macOS Unity Editor, the toolbar will always be displayed in the
    /// window's title bar and the height is fixed.
    /// </summary>
    /// <param name="height">The maximum height value.</param>
    public void SetMaxHeight(float height) {
        currentConfig.MaxHeight = height;
        ApplyCurrentConfig();
    }

    /// <summary>
    /// Applies a toolbar config to fully customize the toolbar layout and behavior.
    ///
    /// This is the recommended way to configure the toolbar. You can control which items appear (back, forward, done,
    /// reload, title, or custom buttons), their order and placement (left, center, right sections), colors, and title
    /// interactions (tap to scroll to top, long press to copy URL).
    ///
    /// After calling this method, the toolbar will be re-rendered with the given config. Pass <c>null</c> to reset to
    /// the default config.
    /// </summary>
    /// <param name="config">The toolbar config to apply. Pass <c>null</c> to apply the default config.</param>
    public void ApplyConfig(UniWebViewEmbeddedToolbarConfig config) {
        currentConfig = CloneConfig(config ?? UniWebViewEmbeddedToolbarConfig.Default);
        ApplyCurrentConfigDirectly();
    }

    private void ApplyCurrentConfig() {
        ApplyCurrentConfigDirectly();
    }

    private void ApplyCurrentConfigDirectly() {
        var json = currentConfig.ToJson();
        if (json == lastAppliedJson) {
            return;
        }

        var applied = UniWebViewEmbeddedToolbarConfigApplier.ApplyJson(listener.Name, json);
        if (!applied) {
            UniWebViewLogger.Instance.Debug("Embedded toolbar config not applied. The native side may not support setEmbeddedToolbarConfig yet.");
            return;
        }
        lastAppliedJson = json;
    }

    private static UniWebViewEmbeddedToolbarConfig CloneConfig(UniWebViewEmbeddedToolbarConfig config) {
        if (config == null) {
            return UniWebViewEmbeddedToolbarConfig.Default;
        }

        var copy = UniWebViewEmbeddedToolbarConfig.FromJson(config.ToJson());
        return copy ?? UniWebViewEmbeddedToolbarConfig.Default;
    }

    private void SetBuiltInTitle(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind kind, string title) {
        var item = currentConfig.FindFirstBuiltInItem(kind);
        if (item == null) {
            return;
        }
        item.Title = title;
    }

    private void SetBuiltInVisible(UniWebViewEmbeddedToolbarConfig.BuiltInItemKind kind, bool visible) {
        var item = currentConfig.FindFirstBuiltInItem(kind);
        if (item == null) {
            return;
        }
        item.Visible = visible;
    }
}
