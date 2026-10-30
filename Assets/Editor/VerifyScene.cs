// Batch-mode scene verification helper.
// Usage: Unity.exe -batchmode -nographics -quit -accept-apiupdate
//        -projectPath <proj> -executeMethod VerifyScene.OpenMainScene
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyScene
{
    public static void OpenMainScene()
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

        var scene = EditorSceneManager.OpenScene(path);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("VERIFY_SCENE_FAILED: could not open " + path);
            EditorApplication.Exit(1);
            return;
        }

        int rootCount = scene.rootCount;
        Debug.Log("VERIFY_SCENE_OK: " + path + " rootObjects=" + rootCount);

        EditorApplication.delayCall += () =>
        {
            EditorApplication.Exit(0);
        };
    }
}
