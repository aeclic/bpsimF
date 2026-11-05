//
//  UniWebViewPopup.cs
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

/// <summary>
/// A lightweight handle to control a popup window created by UniWebView.
/// </summary>
public sealed class UniWebViewPopup {

    private readonly string id;
    private readonly UniWebView owner;
    private bool isAlive = true;

    internal UniWebViewPopup(UniWebView owner, string id) {
        this.owner = owner;
        this.id = id;
    }

    /// <summary>
    /// The identifier of the popup window.
    /// </summary>
    public string Id => id;

    /// <summary>
    /// Whether this popup window is still alive.
    /// </summary>
    public bool IsAlive => isAlive && owner != null;

    /// <summary>
    /// The owner UniWebView that created this popup window.
    /// </summary>
    public UniWebView Owner => owner;

    /// <summary>
    /// Delegate for popup page started event.
    /// </summary>
    /// <param name="popup">The popup window handle.</param>
    /// <param name="url">The url which the popup is about to load.</param>
    public delegate void PopupPageStartedDelegate(UniWebViewPopup popup, string url);
    /// <summary>
    /// Raised when the popup starts loading a url.
    /// </summary>
    public event PopupPageStartedDelegate OnPageStarted;

    /// <summary>
    /// Delegate for popup page committed event.
    /// </summary>
    /// <param name="popup">The popup window handle.</param>
    /// <param name="url">The url which the popup has started receiving content for.</param>
    public delegate void PopupPageCommittedDelegate(UniWebViewPopup popup, string url);
    /// <summary>
    /// Raised when the popup receives response from the server and starts receiving web content.
    /// </summary>
    public event PopupPageCommittedDelegate OnPageCommitted;

    /// <summary>
    /// Delegate for popup page finished event.
    /// </summary>
    /// <param name="popup">The popup window handle.</param>
    /// <param name="payload">The payload which contains the loading result.</param>
    public delegate void PopupPageFinishedDelegate(UniWebViewPopup popup, UniWebViewNativeResultPayload payload);
    /// <summary>
    /// Raised when the popup finishes loading a url.
    /// </summary>
    public event PopupPageFinishedDelegate OnPageFinished;

    /// <summary>
    /// Delegate for popup page loading error received event.
    /// </summary>
    /// <param name="popup">The popup window handle.</param>
    /// <param name="payload">The payload which contains the loading error.</param>
    public delegate void PopupPageErrorReceivedDelegate(UniWebViewPopup popup, UniWebViewNativeResultPayload payload);
    /// <summary>
    /// Raised when an error encountered during the loading process of popup.
    /// </summary>
    public event PopupPageErrorReceivedDelegate OnPageErrorReceived;

    /// <summary>
    /// Delegate for popup page progress changed event.
    /// </summary>
    /// <param name="popup">The popup window handle.</param>
    /// <param name="progress">The loading progress of current popup page.</param>
    public delegate void PopupPageProgressChangedDelegate(UniWebViewPopup popup, float progress);
    /// <summary>
    /// Raised when the loading progress value changes in current popup page.
    /// </summary>
    public event PopupPageProgressChangedDelegate OnPageProgressChanged;

    /// <summary>
    /// Closes the popup window.
    /// </summary>
    public void Close() {
        if (!EnsureAlive()) {
            return;
        }
        owner.InternalClosePopupWindow(id);
    }

    /// <summary>
    /// Navigates back in the popup window history. If it cannot go back, the popup will be closed.
    /// </summary>
    public void GoBack() {
        if (!EnsureAlive()) {
            return;
        }
        owner.InternalGoBackPopupWindow(id);
    }

    /// <summary>
    /// Navigates forward in the popup window history.
    /// </summary>
    public void GoForward() {
        if (!EnsureAlive()) {
            return;
        }
        owner.InternalGoForwardPopupWindow(id);
    }

    /// <summary>
    /// Evaluates a JavaScript string in the popup window context.
    /// </summary>
    public void EvaluateJavaScript(string jsString, Action<UniWebViewNativeResultPayload> completionHandler = null) {
        if (!EnsureAlive(completionHandler)) {
            return;
        }
        owner.InternalEvaluateJavaScriptInPopupWindow(id, jsString, completionHandler);
    }

    internal void MarkClosed() {
        isAlive = false;
    }

    internal void InternalOnPageStarted(string url) {
        if (OnPageStarted != null && IsAlive) {
            OnPageStarted(this, url);
        }
    }

    internal void InternalOnPageCommitted(string url) {
        if (OnPageCommitted != null && IsAlive) {
            OnPageCommitted(this, url);
        }
    }

    internal void InternalOnPageFinished(UniWebViewNativeResultPayload payload) {
        if (OnPageFinished != null && IsAlive) {
            OnPageFinished(this, payload);
        }
    }

    internal void InternalOnPageErrorReceived(UniWebViewNativeResultPayload payload) {
        if (OnPageErrorReceived != null && IsAlive) {
            OnPageErrorReceived(this, payload);
        }
    }

    internal void InternalOnPageProgressChanged(float progress) {
        if (OnPageProgressChanged != null && IsAlive) {
            OnPageProgressChanged(this, progress);
        }
    }

    private bool EnsureAlive(Action<UniWebViewNativeResultPayload> completionHandler = null) {
        if (IsAlive) {
            return true;
        }

        UniWebViewLogger.Instance.Warning("Popup window is already closed or its owner is unavailable.");
        if (completionHandler != null) {
            completionHandler(CreateInvalidPayload("Popup window is unavailable."));
        }
        return false;
    }

    private UniWebViewNativeResultPayload CreateInvalidPayload(string message) {
        return new UniWebViewNativeResultPayload {
            identifier = string.Empty,
            resultCode = "-1",
            data = message,
            extra = string.Empty
        };
    }
}
