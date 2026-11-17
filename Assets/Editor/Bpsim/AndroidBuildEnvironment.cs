using System;
using System.Diagnostics;
using UnityEditor;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Unity 2022 runs sdkmanager --list even with an explicit target SDK. Its default
    /// HTTPS timeout is infinite, so an unreachable repository can block SDK detection
    /// before BuildPipeline callbacks run. Wrap the Build Settings handler itself.
    /// </summary>
    public static class AndroidBuildEnvironment
    {
        [InitializeOnLoadMethod]
        static void RegisterBuildHandler()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(BuildFromBuildSettings);
        }

        public static void BuildFromBuildSettings(BuildPlayerOptions options)
        {
            if (options.target != BuildTarget.Android)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                return;
            }

            BeeEnvironment.CollapseCaseDuplicates();

            using (new SdkNetworkTimeout())
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                }
                finally
                {
                    UnityEngine.Debug.Log($"[AndroidBuild] Build Settings finished in {timer.Elapsed.TotalSeconds:F1}s");
                }
            }
        }

        // SDKMANAGER_OPTS is consumed by sdkmanager.bat, not Gradle. Set it only for
        // this synchronous build and preserve any existing user JVM options.
        internal sealed class SdkNetworkTimeout : IDisposable
        {
            const string Variable = "SDKMANAGER_OPTS";
            const string Timeouts = "-Dsun.net.client.defaultConnectTimeout=3000 -Dsun.net.client.defaultReadTimeout=3000";
            readonly string previous;

            internal SdkNetworkTimeout()
            {
                // The default handler may otherwise defer a platform switch across a domain
                // reload, outliving this synchronous scope. Build Settings exposes Switch Platform.
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new BuildPlayerWindow.BuildMethodException(
                        "Switch Platform to Android and wait for compilation before building the APK.");

                previous = Environment.GetEnvironmentVariable(Variable);
                Environment.SetEnvironmentVariable(Variable,
                    string.IsNullOrWhiteSpace(previous) ? Timeouts : previous + " " + Timeouts);
                UnityEngine.Debug.Log("[AndroidBuild] SDK repository connection/read timeout: 3000 ms");
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(Variable, previous);
            }
        }
    }
}

