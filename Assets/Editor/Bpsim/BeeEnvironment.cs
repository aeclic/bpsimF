using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Keeps the process environment palatable for Unity's Bee build backend.
    ///
    /// Bee's ScriptCompilationBuildProgram copies the whole process environment into a
    /// case-insensitive Hashtable (Bee.Stevedore.Program.EnvironmentPaths..ctor). Windows lets a
    /// process block hold both spellings of a variable, and launchers/agents such as DSH inject
    /// "no_proxy" next to "NO_PROXY" (same for http_proxy/https_proxy). The copy then throws and
    /// the backend dies before any C# is compiled:
    ///
    ///   Internal build system error. BuildProgram exited with code -532462766.
    ///   System.ArgumentException: Item has already been added.
    ///     Key in dictionary: 'no_proxy'  Key being added: 'NO_PROXY'
    ///
    /// BuildPipeline surfaces that as "Error building Player because scripts have compile errors
    /// in the editor", so every player build fails in under a second with a misleading message.
    /// Collapsing each duplicate group to a single variable fixes the backend and leaves the
    /// meaning of every variable unchanged, because Windows environment lookups ignore case.
    /// </summary>
    public static class BeeEnvironment
    {
        const int MaxRemovalsPerVariable = 16;

        [InitializeOnLoadMethod]
        static void CollapseOnLoad()
        {
            CollapseCaseDuplicates();
        }

        /// <summary>
        /// Reduces every group of environment variables that differ only by case to one entry.
        /// Safe to call repeatedly; does nothing once the environment is already clean.
        /// </summary>
        public static void CollapseCaseDuplicates()
        {
            IDictionary environment = Environment.GetEnvironmentVariables();
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (object key in environment.Keys)
            {
                var name = (string)key;
                List<string> names;
                if (!groups.TryGetValue(name, out names))
                {
                    names = new List<string>();
                    groups.Add(name, names);
                }
                names.Add(name);
            }

            var collapsed = new List<string>();
            foreach (KeyValuePair<string, List<string>> group in groups)
            {
                if (group.Value.Count < 2) continue;

                string keep = PreferredName(group.Value);
                string value = (string)environment[keep];

                foreach (string name in group.Value)
                    RemoveEveryVariant(name);

                if (value != null)
                    Environment.SetEnvironmentVariable(keep, value);

                collapsed.Add(string.Join("/", group.Value.ToArray()) + " -> " + keep);
            }

            if (collapsed.Count > 0)
            {
                UnityEngine.Debug.Log("[BeeEnvironment] collapsed case-duplicate environment variables: "
                                      + string.Join(", ", collapsed.ToArray()));
            }
        }

        // Uppercase is the conventional spelling on Windows; anything else keeps its first name.
        static string PreferredName(List<string> names)
        {
            foreach (string name in names)
                if (name == name.ToUpperInvariant()) return name;
            return names[0];
        }

        // SetEnvironmentVariable removes a single case-insensitive match, so repeat until the last
        // variant of this name is gone.
        static void RemoveEveryVariant(string name)
        {
            for (int i = 0; i < MaxRemovalsPerVariable && VariantExists(name); i++)
                Environment.SetEnvironmentVariable(name, null);
        }

        static bool VariantExists(string name)
        {
            foreach (object key in Environment.GetEnvironmentVariables().Keys)
                if (string.Equals((string)key, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
