//
//  UniWebViewSafeBrowsing.cs
//  Created by Wang Wei(@onevcat) on 2020-07-18.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects 
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase. 
//
//  This asset and all corresponding files (such as source code) are provided on an 
//  “as is” basis, without warranty of any kind, express of implied, including but not 
//  limited to the warranties of merchantability, fitness for a particular purpose, and 
//  noninfringement. In no event shall the authors or copyright holders be liable for any 
//  claim, damages or other liability, whether in action of contract, tort or otherwise, 
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//

using UnityEngine;
using System;
using Object = UnityEngine.Object;

/// <summary>
/// UniWebView Safe Browsing provides a way for browsing the web content in a more browser-like way, such as Safari on 
/// iOS and Chrome on Android.
/// 
/// This class wraps `SFSafariViewController` on iOS and "Custom Tabs" on Android. It shares cookies, auto-fill 
/// completion and other more data with the browser on device. Most of permissions are also built-in supported. You can
/// use this class for some tasks that are limited for a normal web view, such as using Apple Pay or Progressive Web 
/// Apps (PWA).
/// 
/// You create a `UniWebViewSafeBrowsing` instance by calling the static `UniWebViewSafeBrowsing.Create` method with a
/// destination URL. You cannot change this URL once the instance is created. To show the safe browsing, call `Show` on
/// the instance. The web content will be displayed in full screen with a toolbar containing the loaded URL, as well
/// as some basic controls like Go Back, Go Forward and Done. 
/// 
/// Browsing web content in `UniWebViewSafeBrowsing` is only supported on iOS and Android. There is no such component in
/// Unity Editor. Creating and showing a `UniWebViewSafeBrowsing` on Unity Editor will fall back to open the URL in 
/// external browser by using Unity's `Application.OpenURL`.
/// 
/// </summary>
public class UniWebViewSafeBrowsing {

    public enum ColorScheme
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    public enum ActivityHeightResizeBehavior
    {
        Default = 0,
        Resizable = 1,
        Fixed = 2
    }

    /// <summary>
    /// Delegate for safe browsing finish event.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    public delegate void OnSafeBrowsingFinishedDelegate(UniWebViewSafeBrowsing browsing);
    
    /// <summary>
    /// Raised when user dismisses safe browsing by tapping the Done button or Back button.
    ///
    /// The dismissed safe browsing instance will be invalid after this event being raised, and you should not use
    /// it for another browsing purpose. Instead, create a new one for a new browsing session.
    ///
    /// This event will not happen in Unity Editor, because the whole `UniWebViewSafeBrowsing` will fall back to an
    /// external browser.
    /// </summary>
    public event OnSafeBrowsingFinishedDelegate OnSafeBrowsingFinished;

    /// <summary>
    /// Delegate for safe browsing close event with metadata payload.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` instance that raised the event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingClosedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised together with <see cref="OnSafeBrowsingFinished"/> but providing metadata.
    /// </summary>
    public event OnSafeBrowsingClosedDelegate OnSafeBrowsingClosed;

    /// <summary>
    /// Delegate for safe browsing started event.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingNavigationStartedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised when website starts page loading in safe browsing
    /// 
    /// This event will not happen in Unity Editor or on iOS, because the native API does not expose a navigation-start callback there.
    /// </summary>
    public event OnSafeBrowsingNavigationStartedDelegate OnSafeBrowsingNavigationStarted;

    /// <summary>
    /// Delegate for safe browsing finished page loading event.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingNavigationFinishedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised when website finishes loading in safe browsing
    /// 
    /// This event will not happen in Unity Editor.
    /// </summary>
    public event OnSafeBrowsingNavigationFinishedDelegate OnSafeBrowsingNavigationFinished;

    /// <summary>
    /// Delegate for safe browsing finished loading event.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingNavigationFailedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised if the website errors while loading
    /// 
    /// This event will not happen in Unity Editor.
    /// </summary>
    public event OnSafeBrowsingNavigationFailedDelegate OnSafeBrowsingNavigationFailed;

    /// <summary>
    /// Delegate for safe browsing warmup complete.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingWarmupCompleteDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised when the safe browser warmup is complete.
    ///
    /// Android only. Requires AndroidX Browser 1.8.0+.
    /// Note this indicates the browser process warmup callback, not that the page finished loading.
    ///
    /// This event will not happen in Unity Editor.
    /// </summary>
    public event OnSafeBrowsingWarmupCompleteDelegate OnSafeBrowsingWarmupComplete;

    /// <summary>
    /// Delegate for safe browsing minimized.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingMinimizedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised when the safe browser is minimized.
    ///
    /// Android only. Requires AndroidX Browser 1.8.0+.
    ///
    /// This event will not happen in Unity Editor.
    /// </summary>
    public event OnSafeBrowsingMinimizedDelegate OnSafeBrowsingMinimized;

    /// <summary>
    /// Delegate for safe browsing unminimized.
    /// </summary>
    /// <param name="browsing">The `UniWebViewSafeBrowsing` object raised this event.</param>
    /// <param name="metadata">Structured metadata describing the native callback.</param>
    public delegate void OnSafeBrowsingUnminimizedDelegate(UniWebViewSafeBrowsing browsing, UniWebViewSafeBrowsingEventMetadata metadata);

    /// <summary>
    /// Raised when the safe browser is restored from minimized state.
    ///
    /// Android only. Requires AndroidX Browser 1.8.0+.
    ///
    /// This event will not happen in Unity Editor.
    /// </summary>
    public event OnSafeBrowsingUnminimizedDelegate OnSafeBrowsingUnminimized;

    private string id = Guid.NewGuid().ToString();
    private UniWebViewNativeListener listener;
    private bool isDisposed;
#if UNITY_IOS && !UNITY_EDITOR
    private bool hasShown;
#endif

    // This is only for editor, to open the url in system browser.
    private string url;

    /// <summary>
    /// Whether the safe browsing mode is supported in current runtime or not.
    /// 
    /// If supported, the safe browsing mode will be used when `Show` is called on a `UniWebViewSafeBrowsing` instance.
    /// Otherwise, the system default browser will be used to open the page when `Show` is called.
    /// 
    /// This property always returns `true` on iOS runtime platform. On Android, it depends on whether there is an Intent 
    /// can handle the safe browsing request. Usually it is provided by Chrome. If there is no Intent can open the URL
    /// in safe browsing mode, this property will return `false`.
    /// 
    /// To use this API on Android when you set your Target SDK to Android 11 or later, you need to declare the correct 
    /// intent query explicitly in your AndroidManifest.xml, to follow the Package Visibility 
    /// (https://developer.android.com/about/versions/11/privacy/package-visibility):
    /// 
    /// ```xml
    /// <queries>
    ///   <intent>
    ///     <action android:name="android.support.customtabs.action.CustomTabsService" />
    ///   </intent>
    /// </queries>
    /// ``` 
    /// </summary>
    /// <returns>
    /// Returns `true` if the safe browsing mode is supported and the page will be opened in safe browsing 
    /// mode. Otherwise, `false`.
    /// </returns>
    public static bool IsSafeBrowsingSupported {
        get {
#if UNITY_EDITOR
            return false;
#elif UNITY_IOS
            return true;
#elif UNITY_ANDROID
            return UniWebViewInterface.IsSafeBrowsingSupported();
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Returns the Android Custom Tabs provider package name that UniWebView Safe Browsing would use.
    /// 
    /// This is Android-only. It returns `null` if Safe Browsing is not supported or no provider can be resolved.
    /// </summary>
    /// <returns>The resolved Custom Tabs provider package name on Android; otherwise, `null`.</returns>
    public static string GetSafeBrowsingCustomTabsProviderPackageName()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return UniWebViewInterface.GetSafeBrowsingCustomTabsProviderPackageName();
#else
        return null;
#endif
    }


    /// <summary>
    /// Sets the preferred browsers for Custom Tabs in order of preference.
    /// This allows developers to specify which browsers should be preferred when multiple
    /// Custom Tabs providers are available on the device.
    /// 
    /// This setting affects both SafeBrowsing and AuthenticationSession functionality.
    /// 
    /// Browser Selection Priority (Android):
    /// 1. User-defined preferred packages (highest priority) - checked in order
    /// 2. Default browser if it's Chromium-based (Chrome, Edge, etc.)
    /// 3. Default browser if it supports Custom Tabs (even non-Chromium)
    /// 4. Any Chromium-based browser (only when no user preference is set)
    /// 5. Any available Custom Tabs provider (last resort)
    /// 
    /// This prioritization helps avoid browsers with incomplete Custom Tabs implementations
    /// (such as Firefox, which may not trigger onNavigationEvent callbacks properly).
    /// 
    /// On iOS, this method has no effect as Safari is always used for safe browsing.
    /// </summary>
    /// <param name="packages">Array of browser package names in order of preference. Common package names include:
    /// - "com.android.chrome" (Chrome)
    /// - "com.brave.browser" (Brave Browser)
    /// - "com.opera.browser" (Opera Browser)
    /// - "com.microsoft.emmx" (Microsoft Edge)
    /// - "com.sec.android.app.sbrowser" (Samsung Internet)
    /// </param>
    public static void SetPreferredCustomTabsBrowsers(string[] packages) {
#if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetPreferredCustomTabsBrowsers(packages);
#endif
    }

    /// <summary>
    /// Creates a new `UniWebViewSafeBrowsing` instance with a given URL.
    /// </summary>
    /// <param name="url">The URL to navigate to. The URL must use the `http` or `https` scheme.</param>
    /// <returns>A newly created `UniWebViewSafeBrowsing` instance.</returns>
    public static UniWebViewSafeBrowsing Create(string url) {
        var safeBrowsing = new UniWebViewSafeBrowsing();
        if (!UniWebViewHelper.IsEditor) {
            safeBrowsing.listener.safeBrowsing = safeBrowsing;
            safeBrowsing.Init(url);
        }
        safeBrowsing.url = url;

        return safeBrowsing;
    }

    /// <summary>
    /// Shows the safe browsing content above current screen.
    /// </summary>
    public void Show() {
        if (UniWebViewSafeBrowsing.IsSafeBrowsingSupported) {
            if (!EnsureNativeInvocationAvailable(nameof(Show))) {
                return;
            }
#if UNITY_IOS && !UNITY_EDITOR
            hasShown = true;
#endif
            UniWebViewInterface.SafeBrowsingShow(listener.Name);
        } else {
            if (!UniWebViewHelper.IsEditor) {
                UniWebViewLogger.Instance.Critical(@"UniWebViewSafeBrowsing.Show is called but the current device does 
                not support Safe Browsing. 
                This might be due to Chrome or any other processing app is not installed, or the manifest file not 
                configured correctly. Check SafeBrowsing Mode guide for more:  https://docs.uniwebview.com/guide/safe-browsing.html");
            }
            Application.OpenURL(url);
        }
    }

    /// <summary>
    /// Dismisses the safe browsing component.
    /// 
    /// This method only works on iOS. On Android, there is no way to dismiss the safe browsing component 
    /// programatically as the result of the limitation from the native (Android) side.
    /// </summary>
    public void Dismiss() {
#if UNITY_IOS && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(Dismiss))) {
            return;
        }
        UniWebViewInterface.SafeBrowsingDismiss(listener.Name);
#endif
    }

    /// <summary>
    /// Invalidates this safe browsing instance and releases underlying native resources without waiting for a native close event.
    /// </summary>
    public void Invalidate() {
        if (isDisposed) {
            UniWebViewLogger.Instance.Debug("Safe Browsing instance already invalidated.");
            return;
        }
#if UNITY_IOS && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(Invalidate))) {
            return;
        }
        UniWebViewInterface.SafeBrowsingInvalidate(listener.Name);
        if (!hasShown) {
            CleanupSafeBrowsing();
        }
#else
        if (!UniWebViewHelper.IsEditor) {
            if (!EnsureNativeInvocationAvailable(nameof(Invalidate))) {
                return;
            }
            UniWebViewInterface.SafeBrowsingInvalidate(listener.Name);
        }
        CleanupSafeBrowsing();
#endif
    }

    /// <summary>
    /// Sets the color for toolbar background in the safe browsing component. The changes are ignored after `Show`
    /// method is called.
    /// </summary>
    /// <param name="color">The color to tint the toolbar.</param>
    public void SetToolbarColor(Color color)
    {
        if (!UniWebViewHelper.IsEditor)
        {
            if (!EnsureNativeInvocationAvailable(nameof(SetToolbarColor)))
            {
                return;
            }
            UniWebViewInterface.SafeBrowsingSetToolbarColor(listener.Name, color.r, color.g, color.b);
        }
    }

    /// <summary>
    /// Sets the color scheme applied to the Custom Tab UI. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="colorScheme">Specify a specific color scheme for the browsing component.</param>
    public void SetColorScheme(ColorScheme colorScheme)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetColorScheme)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetColorScheme(listener.Name, (int)colorScheme);
#endif
    }

    /// <summary>
    /// Changes the target URL before the tab is shown (handy for prefetch/warm-up). Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="url">url to switch to</param>
    public void ChangeUrl(string url)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(ChangeUrl)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingChangeUrl(listener.Name, url);
#endif
    }

    /// <summary>
    /// Sets the secondary (bottom) toolbar color. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="color">The color to tint the secondary toolbar.</param>
    public void SetSecondaryToolbarColor(Color color)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetSecondaryToolbarColor)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetSecondaryToolbarColor(listener.Name, color.r, color.g, color.b);
#endif
    }

    /// <summary>
    /// Sets the navigation bar color. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="color">The color to tint the navigation toolbar.</param>
    public void SetNavigationBarColor(Color color)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetNavigationBarColor)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetNavigationBarColor(listener.Name, color.r, color.g, color.b);
#endif
    }

    /// <summary>
    /// Sets the navigation bar divider color. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="color">The color to tint the navigation toolbar.</param>
    public void SetNavigationBarDividerColor(Color color)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetNavigationBarDividerColor)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetNavigationBarDividerColor(listener.Name, color.r, color.g, color.b);
#endif
    }

    /// <summary>
    /// Sets the toolbar corner radius in dp. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="cornerRadiusDp">Toolbar corner radius dp.</param>
    public void SetToolbarCornerRadiusDp(int cornerRadiusDp)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetToolbarCornerRadiusDp)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetToolbarCornerRadiusDp(listener.Name, cornerRadiusDp);
#endif
    }

    /// <summary>
    /// Sets the Custom Tab Activity's initial height in pixels with a resize behavior.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="initialHeightPx">Partial custom tab initial height in pixels.</param>
    /// <param name="resizeBehavior">Resize behavior applied to the partial custom tab (`Default`, `Resizable`, or `Fixed`).</param>
    public void SetInitialHeightPx(int initialHeightPx, ActivityHeightResizeBehavior resizeBehavior = ActivityHeightResizeBehavior.Fixed)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetInitialHeightPx)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetInitialHeightPx(listener.Name, initialHeightPx, (int)resizeBehavior);
#endif
    }

    /// <summary>
    /// Sets the Custom Tab Activity's initial width in pixels. Defaults to no resize.
    ///
    /// Android only. Requires AndroidX Browser 1.8.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="initialWidthPx">partial custom tab initial width in pixels</param>
    public void SetInitialWidthPx(int initialWidthPx)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetInitialWidthPx)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetInitialWidthPx(listener.Name, initialWidthPx);
#endif
    }
    

    /// <summary>
    /// Enables or disables share menu items. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enabling sharing with other browsers</param>
    public void SetShareMenuItemEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetShareMenuItemEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetShareMenuItemEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Enables URL bar hiding on scroll. Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables url hiding on scroll down</param>
    public void SetUrlBarHidingEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetUrlBarHidingEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetUrlBarHidingEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Sends redirects to the system default handler when enabled. Ignored after `Show`.
    ///
    /// Android only. Requires AndroidX Browser 1.7.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables sending to external handler for links tapped.</param>
    public void SetSendToExternalDefaultHandlerEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetSendToExternalDefaultHandlerEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetSendToExternalDefaultHandlerEnabled(listener.Name, enable);
#endif
    }


    /// <summary>
    /// Toggles the maximization button for partial custom tabs. Ignored after `Show`.
    ///
    /// Android only. Requires AndroidX Browser 1.8.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables whether users can maximize webview when its a partial one.</param>
    public void SetMaximizationEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetMaximizationEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetMaximizationEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Toggles the download button in the overflow menu. Ignored after `Show`.
    ///
    /// Android only. Requires AndroidX Browser 1.7.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables whether users can maximize webview when its a partial one.</param>
    public void SetDownloadButtonEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetDownloadButtonEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetDownloadButtonEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Toggles the bookmarks button in the overflow menu. Enabled by default. Ignored after `Show`.
    ///
    /// Android only. Requires AndroidX Browser 1.7.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables bookmark button</param>
    public void SetBookmarksButtonEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetBookmarksButtonEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetBookmarksButtonEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Allows background interaction when a partial Custom Tab is shown. Ignored after `Show`.
    ///
    /// Android only. Requires AndroidX Browser 1.7.0+. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enables background interactions while partial webview is showing</param>
    public void SetBackgroundInteractionEnabled(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetBackgroundInteractionEnabled)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetBackgroundInteractionEnabled(listener.Name, enable);
#endif
    }

    /// <summary>
    /// Warms up the browser process asynchronously to speed up later `Show`. Can be called multiple times.
    ///
    /// Android only. This method does nothing on iOS.
    /// </summary>
    /// <param name="enable">enable warmup</param>
    public void SetWarmup(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetWarmup)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetWarmup(listener.Name, enable);
#endif
    }
    
    /// Uses Custom Tabs prefetch (mayLaunchUrl). Ignored after `Show`.
    ///
    /// Android only. This method does nothing on iOS.
    /// If `alternativeUrl` is provided, it will be used for prefetching instead of the current URL.
    public void SetPrefetch(bool enable, string alternativeUrl = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetPrefetch)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetPrefetch(listener.Name, enable, alternativeUrl);
#endif
    }

    /// <summary>
    /// Sets the color for toolbar controls in the safe browsing component. The changes are ignored after `Show` method
    /// is called.
    /// 
    /// This method only works on iOS. On Android, the controls color is determined by system to keep a reasonable 
    /// contrast, based on the toolbar background color you provided in `SetToolbarColor`.
    /// </summary>
    /// <param name="color">The color to tint the controls on toolbar.</param>
    public void SetToolbarItemColor(Color color) {
#if UNITY_IOS && !UNITY_EDITOR
        if (!EnsureNativeInvocationAvailable(nameof(SetToolbarItemColor)))
        {
            return;
        }
        UniWebViewInterface.SafeBrowsingSetToolbarItemColor(listener.Name, color.r, color.g, color.b);
#endif
    }
    
    

    private UniWebViewSafeBrowsing() {
        if (!UniWebViewHelper.IsEditor) {
            var listenerObject = new GameObject(id);
            listener = listenerObject.AddComponent<UniWebViewNativeListener>();
            UniWebViewNativeListener.AddListener(listener);
        }
    }

    private void Init(string url) {
        UniWebViewInterface.SafeBrowsingInit(listener.Name, url);
    }

    internal void InternalSafeBrowsingEvent(string metadata) {
        var parsed = UniWebViewSafeBrowsingEventMetadata.FromRaw(
            metadata,
            UniWebViewSafeBrowsingEventMetadata.EventKind.Unknown
        );

        if (!string.IsNullOrEmpty(parsed.Raw)) {
            UniWebViewLogger.Instance.Verbose("Safe Browsing metadata (" + parsed.Kind + "): " + parsed.Raw);
        }

        switch (parsed.Kind) {
            case UniWebViewSafeBrowsingEventMetadata.EventKind.NavigationStarted:
                if (OnSafeBrowsingNavigationStarted != null) {
                    OnSafeBrowsingNavigationStarted(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.NavigationFinished:
                if (OnSafeBrowsingNavigationFinished != null) {
                    OnSafeBrowsingNavigationFinished(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.NavigationFailed:
                if (OnSafeBrowsingNavigationFailed != null) {
                    OnSafeBrowsingNavigationFailed(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.Minimized:
                if (OnSafeBrowsingMinimized != null) {
                    OnSafeBrowsingMinimized(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.Unminimized:
                if (OnSafeBrowsingUnminimized != null) {
                    OnSafeBrowsingUnminimized(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.WarmupComplete:
                if (OnSafeBrowsingWarmupComplete != null) {
                    OnSafeBrowsingWarmupComplete(this, parsed);
                }
                break;
            case UniWebViewSafeBrowsingEventMetadata.EventKind.TabHidden:
                if (OnSafeBrowsingClosed != null) {
                    OnSafeBrowsingClosed(this, parsed);
                }
                if (OnSafeBrowsingFinished != null) {
                    OnSafeBrowsingFinished(this);
                }
                CleanupSafeBrowsing();
                break;
            default:
                UniWebViewLogger.Instance.Debug("Unknown Safe Browsing event metadata: " + parsed.Raw);
                break;
        }
    }

    private bool EnsureNativeInvocationAvailable(string action)
    {
        if (isDisposed || listener == null)
        {
            UniWebViewLogger.Instance.Critical($"Safe Browsing instance already disposed. Skip {action}.");
            return false;
        }
        return true;
    }

    private void CleanupSafeBrowsing() {
        if (isDisposed) {
            return;
        }
        isDisposed = true;
        if (listener == null) {
            return;
        }
        UniWebViewNativeListener.RemoveListener(listener.Name);
        Object.Destroy(listener.gameObject);
        listener = null;
    }
}
