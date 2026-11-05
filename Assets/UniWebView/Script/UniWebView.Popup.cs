//
//  UniWebView.Popup.cs
//  Created by Wang Wei (@onevcat) on 2025-12-28.
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

using System;
using System.Collections.Generic;

public partial class UniWebView {

    private readonly Dictionary<string, UniWebViewPopup> popupWindows
        = new Dictionary<string, UniWebViewPopup>();
    private readonly HashSet<string> closedPopupWindowIds
        = new HashSet<string>();

    /// <summary>
    /// Gets a popup window handle by its identifier.
    /// </summary>
    /// <param name="multipleWindowId">The identifier of the popup window.</param>
    /// <returns>A <see cref="UniWebViewPopup"/> instance if found, otherwise null.</returns>
    public UniWebViewPopup GetPopupWindow(string multipleWindowId) {
        if (string.IsNullOrEmpty(multipleWindowId)) {
            return null;
        }
        UniWebViewPopup popup;
        if (popupWindows.TryGetValue(multipleWindowId, out popup)) {
            return popup;
        }
        return null;
    }

    /// <summary>
    /// Sets whether the popup page events should be delivered to Unity.
    /// </summary>
    /// <param name="enabled">
    /// Whether to enable popup page events. Default is `false`.
    /// </param>
    public void SetPopupPageEventEnabled(bool enabled) {
        UniWebViewInterface.SetPopupPageEventEnabled(listener.Name, enabled);
    }

    internal UniWebViewPopup GetOrCreatePopupWindow(string multipleWindowId) {
        closedPopupWindowIds.Remove(multipleWindowId);
        var existing = GetPopupWindow(multipleWindowId);
        if (existing != null) {
            return existing;
        }
        var popup = new UniWebViewPopup(this, multipleWindowId);
        popupWindows[multipleWindowId] = popup;
        return popup;
    }

    internal void InternalClosePopupWindow(string popupId) {
        UniWebViewInterface.ClosePopupWindow(listener.Name, popupId);
    }

    internal void InternalGoBackPopupWindow(string popupId) {
        UniWebViewInterface.GoBackPopupWindow(listener.Name, popupId);
    }

    internal void InternalGoForwardPopupWindow(string popupId) {
        UniWebViewInterface.GoForwardPopupWindow(listener.Name, popupId);
    }

    internal void InternalEvaluateJavaScriptInPopupWindow(
        string popupId,
        string jsString,
        Action<UniWebViewNativeResultPayload> completionHandler
    ) {
        var identifier = Guid.NewGuid().ToString();
        UniWebViewInterface.EvaluateJavaScriptInPopupWindow(listener.Name, popupId, jsString, identifier);
        if (completionHandler != null) {
            payloadActions.Add(identifier, completionHandler);
        }
    }

    internal void InternalCloseAllPopupWindows() {
        InternalCloseAllPopupWindows(ListenerNameOrId);
    }

    internal void InternalCloseAllPopupWindows(string listenerName) {
        // Internal-only: used by root destroy to emit C# closed events before native cleanup.
        // This path intentionally separates notification (HandlePopupWindowClosed) from native removal,
        // so callers must NOT expose it as a general "close all" API.
        if (popupWindows.Count > 0) {
            var ids = new List<string>(popupWindows.Keys);
            foreach (var popupId in ids) {
                HandlePopupWindowClosed(popupId);
            }
        }
        UniWebViewInterface.CloseAllPopupWindows(listenerName);
    }

    private void HandlePopupWindowClosed(string popupId) {
        if (closedPopupWindowIds.Contains(popupId)) {
            return;
        }
        closedPopupWindowIds.Add(popupId);

        UniWebViewPopup popup;
        if (OnMultipleWindowClosed != null) {
            OnMultipleWindowClosed(this, popupId);
        }

        if (popupWindows.TryGetValue(popupId, out popup)) {
            popup.MarkClosed();
            popupWindows.Remove(popupId);
            if (OnPopupWindowClosed != null) {
                OnPopupWindowClosed(this, popup);
            }
        }
    }

    internal void InternalOnPopupPageStarted(UniWebViewPopupPageEventPayload payload) {
        var popup = GetPopupWindowForEvent(payload);
        if (popup != null) {
            popup.InternalOnPageStarted(payload.url ?? string.Empty);
        }
    }

    internal void InternalOnPopupPageCommitted(UniWebViewPopupPageEventPayload payload) {
        var popup = GetPopupWindowForEvent(payload);
        if (popup != null) {
            popup.InternalOnPageCommitted(payload.url ?? string.Empty);
        }
    }

    internal void InternalOnPopupPageFinished(UniWebViewPopupPageEventPayload payload) {
        var popup = GetPopupWindowForEvent(payload);
        if (popup != null && payload.payload != null) {
            popup.InternalOnPageFinished(payload.payload);
        }
    }

    internal void InternalOnPopupPageErrorReceived(UniWebViewPopupPageEventPayload payload) {
        var popup = GetPopupWindowForEvent(payload);
        if (popup != null && payload.payload != null) {
            popup.InternalOnPageErrorReceived(payload.payload);
        }
    }

    internal void InternalOnPopupPageProgressChanged(UniWebViewPopupPageEventPayload payload) {
        var popup = GetPopupWindowForEvent(payload);
        if (popup != null) {
            popup.InternalOnPageProgressChanged(payload.progress);
        }
    }

    private UniWebViewPopup GetPopupWindowForEvent(UniWebViewPopupPageEventPayload payload) {
        if (payload == null || string.IsNullOrEmpty(payload.popupId)) {
            return null;
        }
        if (closedPopupWindowIds.Contains(payload.popupId)) {
            return null;
        }
        return GetOrCreatePopupWindow(payload.popupId);
    }
}
