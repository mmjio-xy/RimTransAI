using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTransAI.Services;

/// <summary>
/// 定位游戏核心程序集 Assembly-CSharp.dll。
/// RimWorld 1.7 起由团结引擎换成自研引擎（Waylith + .NET 10 自包含），
/// 核心程序集直接放在游戏根目录，不再有 RimWorldWin64_Data\Managed；
/// 1.6 及更早版本仍在 &lt;游戏根目录&gt;\RimWorldWin64_Data\Managed 下。
/// 这里同时兼容新旧两种布局，并支持从 Mod 目录、Mod 源目录、Steam/GOG 常见安装位置自动探测。
/// </summary>
public static class RimWorldAssemblyLocator
{
    public const string AssemblyFileName = "Assembly-CSharp.dll";

    private const int MaxAncestorDepth = 6;

    private static readonly string[] ManagedDataDirectories =
    [
        "RimWorldWin64_Data",
        "RimWorldLinux_Data",
        "RimWorldMac_Data",
        "RimWorld_Data"
    ];

    /// <summary>1.7+ 布局（游戏根目录）与 1.6- 布局（*_Data/Managed）说明，用于界面提示与错误日志。</summary>
    public static string ExpectedLayoutsHint =>
        "RimWorld 1.7 及更高版本：<游戏根目录>\\Assembly-CSharp.dll；" +
        "RimWorld 1.6 及更早版本：<游戏根目录>\\RimWorldWin64_Data\\Managed\\Assembly-CSharp.dll";

    /// <summary>判断给定路径是否指向存在的 Assembly-CSharp.dll。</summary>
    public static bool IsValidAssembly(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(NormalizePath(path));
    }

    /// <summary>
    /// 解析 Assembly-CSharp.dll 的实际路径。
    /// 依次尝试：已配置路径（可指向 dll、游戏根目录或 *_Data/Managed 目录）→ 各提示目录向上回溯 → Steam/GOG 常见安装位置。
    /// </summary>
    public static string? Resolve(string? configuredPath, params string?[] hintDirectories)
    {
        var fromConfig = TryResolvePath(configuredPath);
        if (fromConfig != null)
        {
            return fromConfig;
        }

        foreach (var hint in hintDirectories)
        {
            var found = SearchAncestors(hint);
            if (found != null)
            {
                return found;
            }
        }

        foreach (var gameRoot in EnumerateKnownGameRoots())
        {
            var found = FindInGameRoot(gameRoot);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>从给定目录（通常是 Mod 目录或其上层）向上查找游戏根目录中的核心程序集。</summary>
    public static string? SearchAncestors(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string? current;
        try
        {
            current = Path.GetFullPath(NormalizePath(directory));
        }
        catch (Exception)
        {
            return null;
        }

        for (var depth = 0; depth <= MaxAncestorDepth && !string.IsNullOrEmpty(current); depth++)
        {
            if (Directory.Exists(current))
            {
                var found = FindInGameRoot(current);
                if (found != null)
                {
                    return found;
                }
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>在给定游戏根目录（或 *_Data/Managed 目录）中查找核心程序集。</summary>
    public static string? FindInGameRoot(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return null;
        }

        var direct = Path.Combine(gameRoot, AssemblyFileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        foreach (var dataDirectory in ManagedDataDirectories)
        {
            var candidate = Path.Combine(gameRoot, dataDirectory, "Managed", AssemblyFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>枚举 Steam / GOG 的常见 RimWorld 安装目录（仅返回真实存在的目录）。</summary>
    public static IEnumerable<string> EnumerateKnownGameRoots()
    {
        var roots = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var full = Path.GetFullPath(path);
                if (Directory.Exists(full) && !roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }
            catch (Exception)
            {
                // 非法的环境变量路径直接忽略
            }
        }

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");

        Add(programFilesX86 == null ? null : Path.Combine(programFilesX86, "Steam", "steamapps", "common", "RimWorld"));
        Add(programFiles == null ? null : Path.Combine(programFiles, "Steam", "steamapps", "common", "RimWorld"));
        Add(programFilesX86 == null ? null : Path.Combine(programFilesX86, "GOG Galaxy", "Games", "RimWorld"));

        for (var drive = 'C'; drive <= 'Z'; drive++)
        {
            var prefix = drive + ":\\";
            Add(prefix + "SteamLibrary\\steamapps\\common\\RimWorld");
            Add(prefix + "Steam\\steamapps\\common\\RimWorld");

            if (drive != 'C')
            {
                Add(prefix + "GOG Games\\RimWorld");
            }
        }

        return roots;
    }

    private static string? TryResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = NormalizePath(path);

        try
        {
            if (Directory.Exists(normalized))
            {
                return FindInGameRoot(normalized);
            }

            if (File.Exists(normalized))
            {
                return string.Equals(Path.GetFileName(normalized), AssemblyFileName, StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static string NormalizePath(string path) => path.Trim().Trim('"');
}
