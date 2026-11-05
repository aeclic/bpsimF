//
//  UniWebViewShadow.cs
//  Created by Wang Wei(@onevcat) on 2025-10-31.
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

/// <summary>
/// Represents the shadow styling applied to a UniWebView instance.
/// </summary>
public readonly struct UniWebViewShadow {
    /// <summary>
    /// Shadow blur radius.
    /// </summary>
    public float Radius { get; }

    /// <summary>
    /// Shadow opacity (0 - 1).
    /// </summary>
    public float Opacity { get; }

    /// <summary>
    /// Shadow offset on X axis.
    /// </summary>
    public float OffsetX { get; }

    /// <summary>
    /// Shadow offset on Y axis.
    /// </summary>
    public float OffsetY { get; }

    /// <summary>
    /// Additional spread distance applied to the shadow outline.
    /// </summary>
    public float Spread { get; }

    /// <summary>
    /// Shadow color.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Creates a shadow definition.
    /// </summary>
    public UniWebViewShadow(
        float radius,
        float opacity,
        float offsetX,
        float offsetY,
        Color color,
        float spread = 0.0f
    ) {
        Radius = Mathf.Max(0.0f, radius);
        Opacity = Mathf.Clamp01(opacity);
        OffsetX = offsetX;
        OffsetY = offsetY;
        Color = color;
        Spread = Mathf.Max(0.0f, spread);
    }

    /// <summary>
    /// A shadow definition that disables any shadow drawing.
    /// </summary>
    public static UniWebViewShadow None => new UniWebViewShadow(0.0f, 0.0f, 0.0f, 0.0f, Color.black, 0.0f);

    /// <summary>
    /// Indicates whether the shadow should be rendered.
    /// </summary>
    public bool IsVisible =>
        Opacity > 0.0f &&
        (Radius > 0.0f || Spread > 0.0f);
}
