using System;
using System.IO;
using Bpsim.Mods;

internal static class ModZipClipboardTests
{
	private static int s_passed;

	private static void Check(bool passed, string name)
	{
		if (!passed) throw new Exception("FAILED: " + name);
		s_passed++;
	}

	private static void Accept(string text, string expected, string name)
	{
		string path, error;
		Check(ModZipClipboard.TryNormalizePath(text, out path, out error), name + ": " + error);
		Check(string.Equals(path, expected, StringComparison.OrdinalIgnoreCase), name + " normalized path");
	}

	private static void Reject(string text, string name)
	{
		string path, error;
		Check(!ModZipClipboard.TryNormalizePath(text, out path, out error) && !string.IsNullOrWhiteSpace(error), name);
	}

	private static int Main(string[] args)
	{
		try
		{
			string zip = Path.GetFullPath(args[0]);
			Accept(zip, zip, "raw path");
			Accept(" \r\n" + zip + "\r\n ", zip, "outside whitespace");
			Accept("\"" + zip + "\"", zip, "Explorer Copy as path");
			Accept("'" + zip + "'", zip, "single quotes");
			Accept("<" + zip + ">", zip, "angle brackets");
			Accept("[安装包](<" + zip.Replace('\\', '/') + ">)", zip, "Markdown link");
			Accept(new Uri(zip).AbsoluteUri, zip, "file URI");
			Accept(zip.Replace(" ", "%20"), zip, "encoded spaces");
			Reject(null, "empty clipboard");
			Reject(Path.GetFileName(zip), "filename without directory");
			Reject("https://example.invalid/package.zip", "remote URL");
			Reject(zip + "\n" + zip, "multiple text paths");
			Reject(Path.Combine(Path.GetDirectoryName(zip), "missing-package.zip"), "missing file");
			Reject(Path.Combine(Path.GetDirectoryName(zip), "not-a-zip.txt"), "wrong extension");
			string resolved, error;
			Check(ModZipClipboard.TryResolve("stale text", new[] { zip }, out resolved, out error) && resolved == zip,
				"file-copy clipboard takes precedence over text");
			Check(!ModZipClipboard.TryResolve(zip, new[] { zip, zip }, out resolved, out error) && !string.IsNullOrEmpty(error),
				"multiple zip files are rejected");
			Check(!ModZipClipboard.TryResolve(zip, new[] { "readme.txt" }, out resolved, out error),
				"file clipboard does not fall back to stale text");
			bool nativeZip = ModZipClipboard.TryRead(() => null, out resolved, out error);
			Console.WriteLine("{\"passed\":" + s_passed + ",\"nativeFileClipboardZipDetected\":" +
				(nativeZip ? "true" : "false") + "}");
			return 0;
		}
		catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
	}
}
