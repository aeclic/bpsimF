# -*- coding: utf-8 -*-
# Idempotent patch: silence Burst's "not a known Burst entry point" warnings for precompiled
# vendor plugins (Havok Physics), whose $BurstDirectCall stubs were IL-post-processed by a
# different Burst version. Runtime behaviour is unchanged: the managed body is used either way.
import io, os, sys

DEFAULT = os.path.join("Library", "PackageCache", "com.unity.burst@1.8.4", "Runtime", "BurstCompiler.cs")
MARK = "BPSIM PATCH"

OLD1 = """            OnCompileILPPMethod2?.Invoke();

            var burstMethod = (MethodInfo)MethodBase.GetMethodFromHandle(burstMethodHandle);

            return (IntPtr)Compile(new FakeDelegate(burstMethod), burstMethod, isFunctionPointer: true, isILPostProcessing: true);
"""

NEW1 = """            OnCompileILPPMethod2?.Invoke();

            var burstMethod = (MethodInfo)MethodBase.GetMethodFromHandle(burstMethodHandle);

            // BPSIM PATCH: precompiled vendor assemblies ship $BurstDirectCall stubs whose entry
            // points were IL-post-processed by a different Burst version, so this Burst can never
            // resolve them. Requesting the compile anyway logs "not a known Burst entry point" once
            // per type per domain reload and then falls back to the managed body regardless.
            if (IsPrecompiledPluginAssembly(burstMethod))
            {
                return IntPtr.Zero;
            }

            return (IntPtr)Compile(new FakeDelegate(burstMethod), burstMethod, isFunctionPointer: true, isILPostProcessing: true);
"""

OLD2 = """            if (ilppMethod == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(ilppMethod));
            }
"""

NEW2 = """            // BPSIM PATCH: CompileILPPMethod2 returned IntPtr.Zero for a precompiled vendor
            // assembly; hand back the managed body, exactly like the normal fallback path.
            if (ilppMethod == IntPtr.Zero && managedMethodHandle.Value != IntPtr.Zero)
            {
                var precompiledMethod = (MethodInfo)MethodBase.GetMethodFromHandle(managedMethodHandle);
                if (precompiledMethod != null)
                {
                    return (void*)precompiledMethod.MethodHandle.GetFunctionPointer();
                }
            }

            if (ilppMethod == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(ilppMethod));
            }
"""

OLD3 = """        internal static Action OnCompileILPPMethod2;
"""

NEW3 = """        internal static Action OnCompileILPPMethod2;

        // BPSIM PATCH: assemblies shipped precompiled that carry unusable Burst direct-call stubs.
        // Keep this list in sync with the plugins under Assets/Plugins.
        private static bool IsPrecompiledPluginAssembly(MethodInfo method)
        {
            var assembly = method?.DeclaringType?.Assembly;
            if (assembly == null)
            {
                return false;
            }

            var name = assembly.GetName().Name;
            return name == "Havok.Physics" || name == "Havok.Physics.Hybrid";
        }
"""

def fix(snippet, crlf):
    return snippet.replace("\n", "\r\n") if crlf else snippet

def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
    if not os.path.isfile(path):
        print("[error] not found: " + path)
        return 2
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()
    if MARK in text:
        print("[skip] already patched: " + path)
        return 0
    crlf = "\r\n" in text
    for label, old, new in (("CompileILPPMethod2 body", OLD1, NEW1),
                            ("GetILPPMethodFunctionPointer2 guard", OLD2, NEW2),
                            ("IsPrecompiledPluginAssembly helper", OLD3, NEW3)):
        o = fix(old, crlf)
        if text.count(o) != 1:
            print("[error] anchor not unique (%s): %d matches, crlf=%s" % (label, text.count(o), crlf))
            return 3
        text = text.replace(o, fix(new, crlf), 1)
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(text)
    print("[ok] patched (crlf=%s): %s" % (crlf, path))
    return 0

sys.exit(main())
