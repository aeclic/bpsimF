using UnityEditor;

namespace Bpsim.QA
{
    // Explicit import step for replaying QA after terminal edits when auto-refresh is disabled.
    public static partial class UiThemeQaEditor
    {
        public static string RefreshAssets()
        {
            AssetDatabase.Refresh();
            return "asset refresh requested; inspect compile_status before running UI QA";
        }
    }
}
