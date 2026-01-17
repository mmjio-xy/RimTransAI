using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using RimTransAI.Models;

namespace RimTransAI.Services;

/// <summary>
/// Mod 信息服务，负责解析 About.xml 和加载 Mod 元数据
/// </summary>
public class ModInfoService
{
    /// <summary>
    /// 从 Mod 文件夹加载 Mod 信息
    /// </summary>
    /// <param name="modFolderPath">Mod 文件夹路径</param>
    /// <returns>ModInfo 对象，如果解析失败返回 null</returns>
    public ModInfo? LoadModInfo(string modFolderPath)
    {
        if (string.IsNullOrWhiteSpace(modFolderPath) || !Directory.Exists(modFolderPath))
        {
            Logger.Warning($"Mod 文件夹路径无效: {modFolderPath}");
            return null;
        }

        // About.xml 路径
        var aboutXmlPath = Path.Combine(modFolderPath, "About", "About.xml");
        if (!File.Exists(aboutXmlPath))
        {
            Logger.Warning($"未找到 About.xml: {aboutXmlPath}");
            return null;
        }

        try
        {
            // 解析 About.xml
            var xdoc = XDocument.Load(aboutXmlPath);
            var root = xdoc.Root;

            if (root == null || root.Name.LocalName != "ModMetaData")
            {
                Logger.Warning($"About.xml 格式错误，根节点不是 ModMetaData: {aboutXmlPath}");
                return null;
            }

            var modInfo = new ModInfo
            {
                ModFolderPath = modFolderPath,
                Name = root.Element("name")?.Value ?? string.Empty,
                Author = root.Element("author")?.Value ?? string.Empty,
                Description = root.Element("description")?.Value ?? string.Empty,
                PackageId = root.Element("packageId")?.Value ?? string.Empty,
                Url = root.Element("url")?.Value ?? string.Empty
            };

            // 解析支持的版本列表
            var supportedVersionsElement = root.Element("supportedVersions");
            if (supportedVersionsElement != null)
            {
                modInfo.SupportedVersions = supportedVersionsElement
                    .Elements("li")
                    .Select(e => e.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();
            }

            // 解析依赖关系
            var modDependenciesElement = root.Element("modDependencies");
            if (modDependenciesElement != null)
            {
                modInfo.ModDependencies = modDependenciesElement
                    .Elements("li")
                    .Select(e => new ModDependency
                    {
                        PackageId = e.Element("packageId")?.Value ?? string.Empty,
                        DisplayName = e.Element("displayName")?.Value ?? string.Empty
                    })
                    .Where(d => !string.IsNullOrWhiteSpace(d.PackageId))
                    .ToList();
            }

            // 设置预览图路径
            var previewImagePath = Path.Combine(modFolderPath, "About", "Preview.png");
            modInfo.PreviewImagePath = File.Exists(previewImagePath) ? previewImagePath : string.Empty;

            Logger.Info($"成功加载 Mod 信息: {modInfo.Name}");
            return modInfo;
        }
        catch (Exception ex)
        {
            Logger.Error($"解析 About.xml 失败: {ex.Message}");
            return null;
        }
    }
}