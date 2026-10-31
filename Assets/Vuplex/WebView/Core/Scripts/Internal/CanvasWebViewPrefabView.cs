// Copyright (c) 2025 Vuplex Inc. All rights reserved.
//
// Licensed under the Vuplex Commercial Software Library License, you may
// not use this file except in compliance with the License. You may obtain
// a copy of the License at
//
//     https://vuplex.com/commercial-library-license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using UnityEngine.UI;

namespace Vuplex.WebView.Internal {

    /// <summary>
    /// RawImage that displays the webview texture in CanvasWebViewPrefab.prefab.
    /// </summary>
    /// <remarks>
    /// This type is referenced by CanvasWebViewPrefab.prefab (guid f9b5f2fe5fa80138288d92faddde33a7).
    /// The copy of the package in this project was missing this file, so the prefab's child object
    /// reported "The referenced script on this Behaviour is missing!" and
    /// CanvasViewportMaterialView.Material threw a NullReferenceException because
    /// GetComponent&lt;RawImage&gt;() returned null. Recreated here as a plain RawImage subclass;
    /// all rendering is driven by the CanvasViewportMaterialView component on the same GameObject.
    /// Keep the guid in the .meta file: the prefab binds to the script by guid, not by name.
    /// </remarks>
    public class CanvasWebViewPrefabView : RawImage {
    }
}
