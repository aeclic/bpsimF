#if UNITY_EDITOR
namespace Bpsim.EditorTools
{
    // Explicitly import newly added QA/runtime assets before requesting a script compile.
    public static class SuspensionQaAssets
    {
        public static string Refresh()
        {
            UnityEditor.AssetDatabase.Refresh();
            return "asset refresh requested";
        }
    }
}
#endif
