//
//  UniWebViewChannelMethodEmbeddedToolbarItemAction.cs
//  Created by Wang Wei(@onevcat) on 2026-03-05.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase.
//
//  This asset and all corresponding files (such as source code) are provided on an
//  "as is" basis, without warranty of any kind, express of implied, including but not
//  limited to the warranties of merchantability, fitness for a particular purpose, and
//  noninfringement. In no event shall the authors or copyright holders be liable for any
//  claim, damages or other liability, whether in action of contract, tort or otherwise,
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//

using System;
using UnityEngine;

/// <summary>
/// Represents an embedded toolbar item action request from native side.
/// </summary>
[Serializable]
public class UniWebViewChannelMethodEmbeddedToolbarItemAction {

    [Serializable]
    public enum GestureType {
        Tap,
        LongPress
    }

    internal const string GestureTypeTap = "tap";
    internal const string GestureTypeLongPress = "longPress";

    [SerializeField]
    private string identifier;

    [SerializeField]
    private string gesture;

    [SerializeField]
    private string copyToastText;

    /// <summary>
    /// The toolbar item identifier. Built-in identifiers are prefixed with "uwv.toolbar.".
    /// </summary>
    public string Identifier => identifier;

    /// <summary>
    /// The gesture type from native side. Unsupported values fallback to <see cref="GestureType.Tap"/>.
    /// </summary>
    public GestureType Gesture {
        get {
            if (string.Equals(gesture, GestureTypeLongPress, StringComparison.OrdinalIgnoreCase)) {
                return GestureType.LongPress;
            }
            return GestureType.Tap;
        }
    }

    /// <summary>
    /// Optional toast text from title long-press interaction config.
    /// </summary>
    public string CopyToastText => string.IsNullOrEmpty(copyToastText) ? null : copyToastText;
}
