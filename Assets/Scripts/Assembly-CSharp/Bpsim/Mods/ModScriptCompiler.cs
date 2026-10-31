using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 源码 Mod 的运行时编译（抄 Besiege 的 <c>AssemblyCompiler</c>）。
	/// 目录里有 <c>Code/*.cs</c> 时，把它们编译成 <c>Mods/.compiled/&lt;id&gt;.dll</c>，再走正常加载流程。
	/// </summary>
	/// <remarks>
	/// <para><b>为什么用反射调 CodeDom：</b>Unity 的 .NET Standard 2.1 引用程序集里没有
	/// <c>System.CodeDom</c> / <c>Microsoft.CSharp</c>，直接 <c>using</c> 会编译不过；
	/// 但 Mono 运行时里这两个程序集是存在的。所以按名字反射取用，取不到就返回一个明确的错误，
	/// 让 Mod 作者改用预编译 DLL —— 而不是让整个游戏编译失败。</para>
	/// <para>编译产物按源码时间戳缓存：源码没改就复用上次的 dll，避免每次启动都编译一遍。</para>
	/// </remarks>
	public static class ModScriptCompiler
	{
		/// <summary>源码目录名（相对 Mod 根）。</summary>
		public const string CodeFolderName = "Code";

		/// <summary>这个 Mod 目录里有没有源码。</summary>
		public static bool HasSources(string modDirectory)
		{
			try
			{
				string dir = Path.Combine(modDirectory, CodeFolderName);
				return Directory.Exists(dir) && Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Length > 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>编译（或复用缓存）。成功返回 dll 绝对路径，失败返回 null 并给出原因。</summary>
		public static string Compile(string modDirectory, string modId, out string error)
		{
			error = null;
			string codeDir = Path.Combine(modDirectory, CodeFolderName);
			string[] sources;
			try
			{
				sources = Directory.GetFiles(codeDir, "*.cs", SearchOption.AllDirectories);
			}
			catch (Exception ex)
			{
				error = "读取 Code 目录失败：" + ex.Message;
				return null;
			}
			if (sources.Length == 0)
			{
				error = "Code 目录里没有 .cs 文件";
				return null;
			}

			string outputDir = Path.Combine(ModPaths.Root, ".compiled");
			Directory.CreateDirectory(outputDir);
			string dllPath = Path.Combine(outputDir, Sanitize(modId) + ".dll");

			if (File.Exists(dllPath) && IsUpToDate(sources, dllPath))
			{
				return dllPath;
			}

			object provider;
			Type providerType;
			if (!TryCreateProvider(out provider, out providerType, out error))
			{
				return null;
			}

			Type parametersType = FindType("System.CodeDom.Compiler.CompilerParameters");
			if (parametersType == null)
			{
				error = "运行时缺少 System.CodeDom.Compiler.CompilerParameters";
				return null;
			}

			try
			{
				object parameters = Activator.CreateInstance(parametersType);
				SetProperty(parametersType, parameters, "GenerateExecutable", false);
				SetProperty(parametersType, parameters, "GenerateInMemory", false);
				SetProperty(parametersType, parameters, "OutputAssembly", dllPath);
				SetProperty(parametersType, parameters, "TreatWarningsAsErrors", false);
				List<string> referencePaths = CollectReferencePaths();
				AddReferences(parametersType, parameters, referencePaths);

				MethodInfo compile = providerType.GetMethod("CompileAssemblyFromFile",
					new[] { parametersType, typeof(string[]) });
				if (compile == null)
				{
					error = "CodeDom 提供程序不支持 CompileAssemblyFromFile";
					return null;
				}

				object results = compile.Invoke(provider, new object[] { parameters, sources });
				if (results == null)
				{
					error = "编译器没有返回结果";
					return null;
				}

				Type resultsType = results.GetType();
				object errors = resultsType.GetProperty("Errors")?.GetValue(results);
				if (errors != null && HasErrors(errors, out string messages))
				{
					// 「缺引用」是可恢复的：按错误里的程序集名补上再编一遍。
					// 最常见的就是 netstandard —— 游戏程序集的某个成员带了 [RequiredMember]，
					// 编译器必须同时看到它所在程序集才不会报 CS0012。
					List<string> missing = CollectMissingReferences(errors);
					if (missing.Count > 0 && TryAddMissingReferences(missing, parametersType, parameters, referencePaths))
					{
						results = compile.Invoke(provider, new object[] { parameters, sources });
						if (results != null)
						{
							resultsType = results.GetType();
							errors = resultsType.GetProperty("Errors")?.GetValue(results);
						}
						else
						{
							errors = null;
						}
					}
					if (results == null)
					{
						error = "编译器没有返回结果";
						return null;
					}
					if (errors != null && HasErrors(errors, out string retryMessages))
					{
						error = "源码编译失败：" + retryMessages;
						// 把实际喂给编译器的引用列出来，下次报错能一眼看出是不是又混进了门面程序集。
						Debug.LogWarning("[Mods] 编译 " + modId + " 时使用的引用（" + referencePaths.Count + " 个）："
							+ string.Join(", ", NamesOf(referencePaths)));
						return null;
					}
				}

				return File.Exists(dllPath) ? dllPath : null;
			}
			catch (Exception ex)
			{
				Exception inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
				error = "源码编译异常：" + inner.Message;
				return null;
			}
		}

		/// <summary>把引用列表变成日志里好读的简单名。</summary>
		private static IEnumerable<string> NamesOf(List<string> paths)
		{
			for (int i = 0; i < paths.Count; i++)
			{
				yield return Path.GetFileNameWithoutExtension(paths[i]);
			}
		}

		/// <summary>
		/// 从 CS0012 里捞出「Consider adding a reference to assembly `X, Version=...`」的程序集名。
		/// </summary>
		/// <remarks>
		/// 只认 CS0012：别的错误（语法错、CS0433 类型重复）都不是补引用能解决的，
		/// 盲目补一个门面程序集反而会把 CS0433 引出来。
		/// </remarks>
		private static List<string> CollectMissingReferences(object errors)
		{
			List<string> names = new List<string>();
			if (!(errors is System.Collections.IEnumerable enumerable))
			{
				return names;
			}
			foreach (object item in enumerable)
			{
				if (item == null)
				{
					continue;
				}
				object isWarning = item.GetType().GetProperty("IsWarning")?.GetValue(item);
				if (isWarning is bool warn && warn)
				{
					continue;
				}
				string text = item.ToString();
				const string marker = "Consider adding a reference to assembly";
				int index = text.IndexOf(marker, StringComparison.Ordinal);
				if (index < 0)
				{
					continue;
				}
				int start = index + marker.Length;
				while (start < text.Length && (text[start] == ' ' || text[start] == '`'))
				{
					start++;
				}
				int end = start;
				while (end < text.Length && text[end] != ',' && text[end] != '`' && text[end] != '\'')
				{
					end++;
				}
				string name = text.Substring(start, end - start).Trim();
				if (name.Length > 0 && !names.Contains(name))
				{
					names.Add(name);
				}
			}
			return names;
		}

		/// <summary>
		/// 按名字把缺的程序集找出来接到编译参数上。找不到就不动，交给下一轮报错。
		/// </summary>
		private static bool TryAddMissingReferences(List<string> names, Type parametersType, object parameters, List<string> referencePaths)
		{
			PropertyInfo property = parametersType.GetProperty("ReferencedAssemblies");
			object collection = property?.GetValue(parameters);
			if (collection == null)
			{
				return false;
			}
			MethodInfo add = collection.GetType().GetMethod("Add", new[] { typeof(string) });
			if (add == null)
			{
				return false;
			}

			bool added = false;
			for (int i = 0; i < names.Count; i++)
			{
				// mscorlib 永远不能补：mcs 已经隐式引用了一份，补上必炸 CS0433。
				if (names[i].Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string location = FindLoadedAssemblyPath(names[i]);
				if (location == null)
				{
					Debug.LogWarning("[Mods] 编译需要的程序集 " + names[i] + " 未加载，无法补引用");
					continue;
				}
				try
				{
					add.Invoke(collection, new object[] { location });
					referencePaths.Add(location);
					added = true;
					Debug.Log("[Mods] 编译缺少程序集 " + names[i] + "，已补上：" + location);
				}
				catch (Exception)
				{
				}
			}
			return added;
		}

		// ───────────────────────── 反射细节 ─────────────────────────

		private static bool TryCreateProvider(out object provider, out Type providerType, out string error)
		{
			provider = null;
			error = null;

			// 首选 Microsoft.CSharp.CSharpCodeProvider（Mono 自带）
			providerType = FindType("Microsoft.CSharp.CSharpCodeProvider");
			if (providerType != null)
			{
				try
				{
					provider = Activator.CreateInstance(providerType);
					if (provider != null)
					{
						return true;
					}
				}
				catch (Exception)
				{
					provider = null;
				}
			}

			// 回退：CodeDomProvider.CreateProvider("CSharp")
			Type codeDomProvider = FindType("System.CodeDom.Compiler.CodeDomProvider");
			MethodInfo create = codeDomProvider?.GetMethod("CreateProvider", new[] { typeof(string) });
			if (create != null)
			{
				try
				{
					provider = create.Invoke(null, new object[] { "CSharp" });
					if (provider != null)
					{
						providerType = provider.GetType();
						return true;
					}
				}
				catch (Exception)
				{
					provider = null;
				}
			}

			error = "当前运行时没有 C# 代码编译器（Microsoft.CSharp / CodeDom 均不可用）。"
				+ "请把 Mod 编译成 DLL 后放进目录，改用 <Assemblies> 声明。";
			return false;
		}

		/// <summary>
		/// 给编译参数挂引用。
		/// </summary>
		/// <remarks>
		/// <para>引用列表按「简单名」去重后逐个挂上；<c>mscorlib</c> 永远不会出现在里面，
		/// 原因见 <see cref="CollectReferencePaths"/>。</para>
		/// </remarks>
		private static void AddReferences(Type parametersType, object parameters, List<string> referencePaths)
		{
			PropertyInfo property = parametersType.GetProperty("ReferencedAssemblies");
			object collection = property?.GetValue(parameters);
			if (collection == null)
			{
				return;
			}
			MethodInfo add = collection.GetType().GetMethod("Add", new[] { typeof(string) });
			if (add == null)
			{
				return;
			}

			// 按「简单程序集名」去重：同一份 dll 可能被不同路径加载两次。
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string location in referencePaths)
			{
				string simple = Path.GetFileNameWithoutExtension(location);
				if (!seen.Add(simple))
				{
					continue;
				}
				try
				{
					add.Invoke(collection, new object[] { location });
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>收集要交给编译器的程序集路径。</summary>
		/// <remarks>
		/// <para><b>为什么绝对不能显式引用 mscorlib：</b>Unity 运行时用的 CodeDom 后端是
		/// Mono 的 <c>mcs</c>，它【总是】隐式引用自己那一份
		/// <c>MonoBleedingEdge/lib/mono/4.5/mscorlib.dll</c>。如果再显式挂上运行时的
		/// <c>unityjit-win32/mscorlib.dll</c>，编译器就看到两个都定义
		/// <c>System.Exception</c> 的核心库，立刻报 CS0433「The imported type
		/// `System.Exception' is defined multiple times」。所以这里必须跳过 mscorlib。</para>
		/// <para><b>为什么必须引用 netstandard：</b>Unity 2022 的程序集里有成员带
		/// <c>[RequiredMember]</c> 这类定义在 netstandard 里的特性。缺了它，Mod 源码
		/// 一碰这些成员就报 CS0012「The type `System.Object' is defined in an assembly
		/// that is not referenced ... netstandard」。补上 netstandard 后 CS0012 消失，
		/// 而且不会反过来引出 CS0433（netstandard 只是转发，不重复定义核心类型）。</para>
		/// <para>其余按需收集：Unity 各模块、游戏本体、第三方库全部加；编辑器程序集
		/// 和编译 API 本身排除。</para>
		/// </remarks>
		private static List<string> CollectReferencePaths()
		{
			List<string> paths = new List<string>();

			// 1) netstandard 门面：修 CS0012 的关键，必须排在前面。
			AddPath(paths, FindNetStandardAssembly());

			// 2) 游戏本体：Mod 要能引用 Bpsim.* 的类型。
			AddPath(paths, typeof(ModScriptCompiler).Assembly);

			// 3) 其余已加载程序集，只排除编辑器侧、编译 API 和 mscorlib。
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (!IsCandidateReference(assembly))
				{
					continue;
				}
				AddPath(paths, assembly);
			}
			return paths;
		}

		/// <summary>
		/// 找出运行时真正在用的 netstandard 门面。
		/// </summary>
		/// <remarks>
		/// 优先用 AppDomain 里已经加载的那份（就是运行时的 <c>unityjit-win32/Facades/netstandard.dll</c>），
		/// 找不到再退回 Unity 引用程序集目录里的 4.8-api 门面。两者都能修 CS0012，
		/// 但必须是【转发门面】而不是 <c>NetStandard/ref/2.1.0/netstandard.dll</c> ——
		/// 后者自带一套完整定义，和 mcs 隐式引用的 mscorlib 撞车会炸 CS0433。
		/// </remarks>
		private static Assembly FindNetStandardAssembly()
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				string simple;
				try
				{
					simple = assembly.GetName().Name;
				}
				catch (Exception)
				{
					continue;
				}
				if (string.Equals(simple, "netstandard", StringComparison.OrdinalIgnoreCase))
				{
					return assembly;
				}
			}
			return null;
		}

		/// <summary>
		/// 能不能把 <paramref name="assembly"/> 塞进 Mod 的引用列表。
		/// </summary>
		private static bool IsCandidateReference(Assembly assembly)
		{
			AssemblyName name;
			try
			{
				name = assembly.GetName();
			}
			catch (Exception)
			{
				return false;
			}
			string simple = name.Name;
			if (string.IsNullOrEmpty(simple))
			{
				return false;
			}

			// mscorlib：mcs 已经隐式引用了一份，再挂就是 CS0433。
			if (simple.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
				|| simple.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			// 编译 API 本身不该暴露给 Mod（扫描器也会拦）。
			if (simple.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
				|| simple.Equals("Mono.CSharp", StringComparison.OrdinalIgnoreCase)
				|| simple.Equals("Mono.Cecil", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			// 编辑器程序集：Mod 不该用，而且 UnityEditor 也有门面重复的老问题。
			if (simple.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase)
				|| simple.Equals("Assembly-CSharp-Editor", StringComparison.OrdinalIgnoreCase)
				|| simple.Equals("Assembly-CSharp-Editor-firstpass", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return true;
		}

		private static void AddPath(List<string> paths, Assembly assembly)
		{
			if (assembly == null)
			{
				return;
			}
			string location;
			try
			{
				location = assembly.Location;
			}
			catch (Exception)
			{
				return;
			}
			if (!string.IsNullOrEmpty(location))
			{
				paths.Add(location);
			}
		}
		/// <summary>在已加载的程序集里按简单名找 dll 路径。</summary>
		private static string FindLoadedAssemblyPath(string simpleName)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				string simple;
				try
				{
					simple = assembly.GetName().Name;
				}
				catch (Exception)
				{
					continue;
				}
				if (!string.Equals(simple, simpleName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string location;
				try
				{
					location = assembly.Location;
				}
				catch (Exception)
				{
					continue;
				}
				if (!string.IsNullOrEmpty(location))
				{
					return location;
				}
			}
			return null;
		}

		private static bool HasErrors(object errors, out string messages)
		{
			messages = null;
			Type type = errors.GetType();
			object hasErrors = type.GetProperty("HasErrors")?.GetValue(errors);
			if (!(hasErrors is bool flag) || !flag)
			{
				return false;
			}
			List<string> lines = new List<string>();
			if (errors is System.Collections.IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					if (item == null)
					{
						continue;
					}
					string text = item.ToString();
					object isWarning = item.GetType().GetProperty("IsWarning")?.GetValue(item);
					if (isWarning is bool warn && warn)
					{
						continue;
					}
					lines.Add(text);
					if (lines.Count >= 6)
					{
						break;
					}
				}
			}
			messages = lines.Count > 0 ? string.Join("；", lines.ToArray()) : "未知编译错误";
			return true;
		}

		private static void SetProperty(Type type, object instance, string name, object value)
		{
			PropertyInfo property = type.GetProperty(name);
			if (property != null && property.CanWrite)
			{
				property.SetValue(instance, value);
			}
		}

		/// <summary>按全名找类型，找不到再扫一遍已加载程序集。</summary>
		private static Type FindType(string fullName)
		{
			Type direct = Type.GetType(fullName);
			if (direct != null)
			{
				return direct;
			}
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					Type type = assembly.GetType(fullName, false);
					if (type != null)
					{
						return type;
					}
				}
				catch (Exception)
				{
				}
			}
			return null;
		}

		private static bool IsUpToDate(string[] sources, string dllPath)
		{
			try
			{
				DateTime dllTime = File.GetLastWriteTimeUtc(dllPath);
				foreach (string source in sources)
				{
					if (File.GetLastWriteTimeUtc(source) > dllTime)
					{
						return false;
					}
				}
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static string Sanitize(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return "mod";
			}
			char[] buffer = id.ToCharArray();
			for (int i = 0; i < buffer.Length; i++)
			{
				char c = buffer[i];
				bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
				if (!ok)
				{
					buffer[i] = '_';
				}
			}
			return new string(buffer);
		}
	}
}
