using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>扫描结果。</summary>
	public sealed class ModScanResult
	{
		public bool Passed = true;

		/// <summary>硬性违规：命中任何一条都不加载。</summary>
		public readonly List<string> Violations = new List<string>();

		/// <summary>软性告警：默认只记日志；<see cref="ModdingConfig.StrictScan"/> 开启时升级为违规。</summary>
		public readonly List<string> Warnings = new List<string>();

		public void Violate(string message)
		{
			Passed = false;
			if (Violations.Count < 16)
			{
				Violations.Add(message);
			}
		}

		public void Warn(string message)
		{
			if (Warnings.Count < 16)
			{
				Warnings.Add(message);
			}
		}

		public string Describe()
		{
			if (Passed && Warnings.Count == 0)
			{
				return "通过";
			}
			string head = Passed ? "通过（有告警）" : "被拒绝";
			return head + "：" + string.Join("；", Passed ? Warnings.ToArray() : Violations.ToArray());
		}
	}

	/// <summary>
	/// Mod 程序集静态扫描（抄 Besiege 的 <c>AssemblyScanner</c>，策略按 bpsim 的实际情况调过）。
	/// </summary>
	/// <remarks>
	/// <para>三层检查：</para>
	/// <list type="number">
	/// <item>引用程序集黑名单 —— 拦掉 Steamworks / Mono.Cecil / Roslyn 这类「Mod 不该碰」的依赖。</item>
	/// <item>签名级检查 —— 类型、基类、接口、字段、属性、方法参数与返回值里出现的禁止命名空间，以及 PInvoke。</item>
	/// <item>IL 启发式 —— 扫方法体里的成员引用 token，命中禁止命名空间只告警（严格模式才拒绝）。</item>
	/// </list>
	/// <para>第 3 层故意做成「默认只告警」：它不是精确的 IL 解码，靠 token 位置扫描会有误报，
	/// 用误报去拒绝一个正常 Mod 比放过一个可疑 Mod 更糟。真正拦得住东西的是前两层。</para>
	/// <para>扫描发生在 <c>Assembly.LoadFrom</c> 之后 —— Mono 无法卸载程序集，所以「先加载再扫描」
	/// 是唯一不引入 Cecil 依赖的做法。扫描不过的 Mod 不会实例化入口，也不会注册任何东西。</para>
	/// </remarks>
	public static class ModAssemblyScanner
	{
		/// <summary>禁止引用的程序集（前缀匹配，忽略大小写）。</summary>
		private static readonly string[] ForbiddenAssemblies =
		{
			"Steamworks", "ModIO", "PlayFab", "GameGrind", "Mono.Cecil", "Mono.CSharp",
			"Microsoft.CodeAnalysis", "System.Reflection.Emit", "IKVM", "CSharpCompiler"
		};

		/// <summary>禁止出现的命名空间（硬拒）。</summary>
		private static readonly string[] ForbiddenNamespaces =
		{
			"System.Reflection.Emit",
			"System.Runtime.InteropServices",
			"System.Diagnostics.Process",
			"System.Net.Sockets",
			"System.Net.Http",
			"System.Net.NetworkInformation",
			"Microsoft.Win32",
			"Mono.Cecil",
			"Mono.CSharp",
			"Microsoft.CodeAnalysis"
		};

		/// <summary>敏感但放行的命名空间（只告警）。</summary>
		private static readonly string[] SensitiveNamespaces =
		{
			"System.IO",
			"System.Net",
			"System.Reflection",
			"System.Diagnostics",
			"System.Security",
			"System.Xml"
		};

		/// <summary>敏感命名空间里的白名单类型（精确或前缀匹配）。</summary>
		private static readonly string[] WhitelistedTypes =
		{
			"System.IO.Path",
			"System.IO.Stream",
			"System.IO.MemoryStream",
			"System.IO.FileStream",
			"System.IO.TextReader",
			"System.IO.TextWriter",
			"System.IO.BinaryReader",
			"System.IO.BinaryWriter",
			"System.IO.SeekOrigin",
			"System.IO.Compression",
			"System.Diagnostics.Stopwatch",
			"System.Security.Cryptography"
		};

		/// <summary>IL 里带 4 字节 metadata token 的常见操作码。</summary>
		private static readonly byte[] TokenOpcodes =
		{
			0x28, // call
			0x6F, // callvirt
			0x73, // newobj
			0x72, // ldstr
			0x74, // castclass
			0x75, // isinst
			0x7B, // ldfld
			0x7C, // ldflda
			0x7D, // stfld
			0x7E, // ldsfld
			0x7F, // ldsflda
			0x80, // stsfld
			0xD0, // ldtoken
			0x8C, // box
			0xA5  // unbox.any
		};

		/// <summary>扫描一个已加载的程序集。</summary>
		public static ModScanResult Scan(Assembly assembly, bool strict)
		{
			ModScanResult result = new ModScanResult();
			if (assembly == null)
			{
				result.Violate("程序集为空");
				return result;
			}

			ScanReferences(assembly, result);
			Type[] types = SafeGetTypes(assembly, result);
			foreach (Type type in types)
			{
				if (type == null)
				{
					continue;
				}
				ScanType(type, result, strict);
			}

			if (strict)
			{
				foreach (string warning in result.Warnings)
				{
					result.Violate("严格模式：" + warning);
				}
			}
			return result;
		}

		private static void ScanReferences(Assembly assembly, ModScanResult result)
		{
			try
			{
				foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
				{
					string name = reference.Name ?? string.Empty;
					foreach (string forbidden in ForbiddenAssemblies)
					{
						if (name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
						{
							result.Violate("引用了禁止的程序集 " + name);
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				result.Warn("读取引用列表失败：" + ex.Message);
			}
		}

		private static Type[] SafeGetTypes(Assembly assembly, ModScanResult result)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				result.Warn("部分类型无法加载（" + ex.LoaderExceptions.Length + " 个）");
				return ex.Types ?? new Type[0];
			}
			catch (Exception ex)
			{
				result.Violate("无法枚举类型：" + ex.Message);
				return new Type[0];
			}
		}

		private static void ScanType(Type type, ModScanResult result, bool strict)
		{
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

			Check(type.BaseType, "基类 " + type.FullName, result);
			try
			{
				foreach (Type itf in type.GetInterfaces())
				{
					Check(itf, "接口 " + type.FullName, result);
				}
			}
			catch (Exception)
			{
			}

			FieldInfo[] fields;
			PropertyInfo[] properties;
			MethodInfo[] methods;
			ConstructorInfo[] ctors;
			try
			{
				fields = type.GetFields(all);
				properties = type.GetProperties(all);
				methods = type.GetMethods(all);
				ctors = type.GetConstructors(all);
			}
			catch (Exception ex)
			{
				result.Warn("读取 " + type.FullName + " 成员失败：" + ex.Message);
				return;
			}

			foreach (FieldInfo field in fields)
			{
				Check(field.FieldType, "字段 " + type.FullName + "." + field.Name, result);
			}
			foreach (PropertyInfo property in properties)
			{
				Check(property.PropertyType, "属性 " + type.FullName + "." + property.Name, result);
			}
			foreach (MethodInfo method in methods)
			{
				CheckMethod(method, type, result, strict);
			}
			foreach (ConstructorInfo ctor in ctors)
			{
				CheckMethod(ctor, type, result, strict);
			}
		}

		private static void CheckMethod(MethodBase method, Type owner, ModScanResult result, bool strict)
		{
			string label = owner.FullName + "." + method.Name;

			if (method is MethodInfo info)
			{
				Check(info.ReturnType, "返回值 " + label, result);
			}
			try
			{
				foreach (ParameterInfo parameter in method.GetParameters())
				{
					Check(parameter.ParameterType, "参数 " + label, result);
				}
			}
			catch (Exception)
			{
			}

			if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
			{
				result.Violate("禁止 PInvoke：" + label);
			}
			try
			{
				foreach (object attribute in method.GetCustomAttributes(false))
				{
					if (attribute != null && attribute.GetType().Name == "DllImportAttribute")
					{
						result.Violate("禁止 DllImport：" + label);
					}
				}
			}
			catch (Exception)
			{
			}

			ScanIl(method, label, result, strict);
		}

		/// <summary>IL 启发式：按 token 位置扫成员引用。只告警。</summary>
		private static void ScanIl(MethodBase method, string label, ModScanResult result, bool strict)
		{
			byte[] il;
			System.Reflection.Module module;
			try
			{
				MethodBody body = method.GetMethodBody();
				if (body == null)
				{
					return;
				}
				il = body.GetILAsByteArray();
				module = method.Module;
			}
			catch (Exception)
			{
				return;
			}
			if (il == null || module == null)
			{
				return;
			}

			for (int i = 0; i + 4 < il.Length; i++)
			{
				if (!IsTokenOpcode(il[i]))
				{
					continue;
				}
				int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
				if (token <= 0)
				{
					continue;
				}
				MemberInfo member;
				try
				{
					member = module.ResolveMember(token);
				}
				catch (Exception)
				{
					continue;
				}
				Type declaring = DeclaringTypeOf(member);
				if (declaring == null)
				{
					continue;
				}
				string namespaceHit = ForbiddenNamespaceOf(declaring);
				if (namespaceHit != null)
				{
					result.Warn("IL 里引用了 " + declaring.FullName + "（" + label + "）");
				}
			}
		}

		private static bool IsTokenOpcode(byte opcode)
		{
			for (int i = 0; i < TokenOpcodes.Length; i++)
			{
				if (TokenOpcodes[i] == opcode)
				{
					return true;
				}
			}
			return false;
		}

		private static Type DeclaringTypeOf(MemberInfo member)
		{
			if (member == null)
			{
				return null;
			}
			if (member is Type t)
			{
				return t;
			}
			if (member is MethodBase mb)
			{
				return mb.DeclaringType;
			}
			if (member is FieldInfo fi)
			{
				return fi.DeclaringType;
			}
			if (member is PropertyInfo pi)
			{
				return pi.DeclaringType;
			}
			if (member is EventInfo ei)
			{
				return ei.DeclaringType;
			}
			return null;
		}

		/// <summary>检查一个类型是否落在禁止/敏感命名空间里。</summary>
		private static void Check(Type type, string where, ModScanResult result)
		{
			if (type == null)
			{
				return;
			}
			if (type.HasElementType)
			{
				Check(type.GetElementType(), where, result);
				return;
			}
			if (type.IsGenericParameter)
			{
				return;
			}
			if (type.IsGenericType)
			{
				foreach (Type arg in type.GetGenericArguments())
				{
					Check(arg, where, result);
				}
			}

			string full = type.FullName;
			if (string.IsNullOrEmpty(full))
			{
				return;
			}
			if (IsWhitelisted(full))
			{
				return;
			}

			string forbidden = ForbiddenNamespaceOf(type);
			if (forbidden != null)
			{
				result.Violate(where + " 使用了 " + full);
				return;
			}

			foreach (string sensitive in SensitiveNamespaces)
			{
				if (full.StartsWith(sensitive, StringComparison.Ordinal))
				{
					result.Warn(where + " 触及敏感类型 " + full);
					return;
				}
			}
		}

		private static bool IsWhitelisted(string fullName)
		{
			foreach (string allowed in WhitelistedTypes)
			{
				if (fullName.Equals(allowed, StringComparison.Ordinal)
					|| fullName.StartsWith(allowed + ".", StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static string ForbiddenNamespaceOf(Type type)
		{
			string full = type.FullName;
			if (string.IsNullOrEmpty(full))
			{
				return null;
			}
			foreach (string forbidden in ForbiddenNamespaces)
			{
				if (full.StartsWith(forbidden, StringComparison.Ordinal))
				{
					return forbidden;
				}
			}
			return null;
		}
	}
}
