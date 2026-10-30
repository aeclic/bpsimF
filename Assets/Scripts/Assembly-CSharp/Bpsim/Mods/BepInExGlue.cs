using Bpsim.Modding;

namespace Bpsim.Mods.BepInExHost
{
    // Chainloader entry points live in BepInEx/plugins DLLs, never in Assembly-CSharp.
    // This facade has no BepInEx dependency and never scans or reloads plugin assemblies.
    public static class BpsimModHost
    {
        public static bool Ready => ModManager.Instance != null && ModManager.Instance.Initialized;
        public static void Ensure() => ModManager.Ensure();
        public static bool IsModLoaded(string id) => ModManager.Instance?.Find(id)?.IsLoaded == true;
        public static void Register(ModManifest manifest, IBpsimMod instance, string directory, bool defaultEnabled = false)
            => ModManager.Ensure().RegisterExternal(manifest, instance, directory, defaultEnabled);
        public static void Unregister(string id, IBpsimMod instance)
            => ModManager.Instance?.UnregisterExternal(id, instance);
    }
}