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
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
using UnityEditor;
using UnityEngine;
using Vuplex.WebView.Internal;

namespace Vuplex.WebView.Editor {

    /// <summary>
    /// Editor script for Windows and macOS that checks if the editor preference
    /// "Script Changes While Playing" is set to "Recompile And Continue Playing" and,
    /// if so, automatically changes it "Recompile After Finished Playing".
    /// </summary>
    [InitializeOnLoad]
    static class StandaloneEditorScript {

        static StandaloneEditorScript() {

            _checkReloadDomainSetting();
            _checkScriptCompilationSetting();
        }

        const int RecompileAndContinuePlaying = 0;
        const int RecompileAfterFinishedPlaying = 1;
        const string ScriptCompilationDuringPlay = "ScriptCompilationDuringPlay";

        static void _checkReloadDomainSetting() {

            #if UNITY_2020_1_OR_NEWER
                if (EditorSettings.enterPlayModeOptionsEnabled &&
                    EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload)) {
                    WebViewLogger.LogError("In Editor Settings, domain reload when entering play mode is disabled. This is a setting that 3D WebView doesn't currently support, so it won't work correctly in the editor. To fix this, please go to \"Project Settings\" -> \"Editor Settings\" and either disable \"Enter Play Mode Options\" or enable \"Reload Domain\". The following page describes how scripts must be modified to explicitly support disabling domain reload, which is why 3D WebView doesn't currently support it: https://docs.unity3d.com/Manual/domain-reloading.html");
                }
            #endif
        }

        static void _checkScriptCompilationSetting() {

            #if !VUPLEX_IGNORE_SCRIPT_COMPILATION_SETTING
                // https://support.unity.com/hc/en-us/articles/210452343
                var setting = EditorPrefs.GetInt(ScriptCompilationDuringPlay);
                if (setting == RecompileAndContinuePlaying) {
                    EditorPrefs.SetInt(ScriptCompilationDuringPlay, RecompileAfterFinishedPlaying);
                    WebViewLogger.LogWarning("Just a heads-up: 3D WebView automatically changed the editor preference for \"Script Changes While Playing\" from \"Recompile And Continue Playing\" to \"Recompile After Finished Playing\" because 3D WebView doesn't currently support the former (it causes the editor to crash).");
                }
            #endif
        }
    }
}
#endif
