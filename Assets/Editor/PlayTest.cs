// Headless Play-mode smoke test.
// Usage: Unity.exe -batchmode -nographics -quit -accept-apiupdate
//        -projectPath <proj> -executeMethod PlayTest.Run
// Exits 0 if Play mode runs for the given frame count without exceptions,
// exits 1 if managed errors were logged.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayTest
{
    private static readonly List<string> s_errors = new List<string>();
    private static int s_frames;
    private static double s_startTime;
    private static bool s_enteredPlay;
    private static bool s_finished;

    public static void Run()
    {
        string path = null;
        if (EditorBuildSettings.scenes != null && EditorBuildSettings.scenes.Length > 0)
        {
            path = EditorBuildSettings.scenes[0].path;
        }
        if (string.IsNullOrEmpty(path))
        {
            path = "Assets/Scenes/MainScene.unity";
        }

        Application.logMessageReceived += (condition, stackTrace, type) =>
        {
            if (s_finished)
            {
                return; // otherwise reporting the errors below would collect more of them
            }
            if (type == LogType.Error || type == LogType.Exception)
            {
                if (condition.Contains("OnParticleUpdateJobScheduled"))
                {
                    return; // known harmless serialized callback noise
                }
                s_errors.Add(condition);
            }
        };

        EditorApplication.playModeStateChanged += (state) =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                s_enteredPlay = true;
                Debug.Log("PLAYTEST_ENTERED_PLAY");
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Tick;
                Finish();
            }
        };

        var scene = EditorSceneManager.OpenScene(path);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("PLAYTEST_FAILED: could not open scene " + path);
            EditorApplication.Exit(1);
            return;
        }

        s_startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Debug.Log("PLAYTEST_START");
        EditorApplication.isPlaying = true; // direct: delayCall never fires before -quit in batch mode
    }

    private static void Tick()
    {
        if (s_finished)
        {
            return;
        }
        if (s_enteredPlay)
        {
            s_frames++;
        }
        double elapsed = EditorApplication.timeSinceStartup - s_startTime;
        if ((s_enteredPlay && s_frames >= 240) || elapsed > 120.0)
        {
            Finish();
        }
    }

    private static void Finish()
    {
        if (s_finished)
        {
            return;
        }
        s_finished = true;
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }
        if (s_errors.Count > 0)
        {
            // Snapshot and de-duplicate before logging: Debug.LogError itself raises
            // logMessageReceived, and every extra frame can log the same fault hundreds of times.
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var order = new System.Collections.Generic.List<string>();
            foreach (var e in s_errors)
            {
                int n;
                if (counts.TryGetValue(e, out n))
                {
                    counts[e] = n + 1;
                }
                else
                {
                    counts[e] = 1;
                    order.Add(e);
                }
            }
            s_errors.Clear();
            foreach (var e in order)
            {
                Debug.LogError("PLAYTEST_ERROR x" + counts[e] + ": " + e);
            }
            Debug.LogError("PLAYTEST_FAILED distinct=" + order.Count + " total=" + s_frames + " frames=" + s_frames);
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log("PLAYTEST_OK frames=" + s_frames + " enteredPlay=" + s_enteredPlay);
            EditorApplication.Exit(0);
        }
    }
}
