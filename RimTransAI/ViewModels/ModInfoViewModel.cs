using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Models;

namespace RimTransAI.ViewModels;

public partial class ModInfoViewModel : ViewModelBase
{
    // 基础信息
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private Bitmap? _previewImage;

    // 扩展信息
    [ObservableProperty]
    private string _packageId = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    // 支持版本列表
    public ObservableCollection<string> SupportedVersions { get; } = new();

    // Mod 路径
    [ObservableProperty]
    private string _folderPath = string.Empty;

    // 依赖关系列表
    public ObservableCollection<ModDependency> Dependencies { get; } = new();

    // 辅助属性
    public bool HasUrl => !string.IsNullOrEmpty(Url);
    public bool HasSupportedVersions => SupportedVersions.Count > 0;
    public bool HasDependencies => Dependencies.Count > 0;
    public string DependenciesHeader => $"依赖关系 ({Dependencies.Count})";

    /// <summary>
    /// 从 ModInfo 加载数据
    /// </summary>
    public void LoadFromModInfo(ModInfo modInfo)
    {
        Name = modInfo.Name;
        Author = modInfo.Author;
        Description = modInfo.Description;

        // 加载预览图
        try
        {
            if (!string.IsNullOrEmpty(modInfo.PreviewImagePath) && System.IO.File.Exists(modInfo.PreviewImagePath))
            {
                // 从本地文件加载 Bitmap
                PreviewImage = ResizeImage(new Bitmap(modInfo.PreviewImagePath),400, 200);
            }
            else
            {
                // 加载默认占位图（从嵌入资源）
                var assets = AssetLoader.Open(new Uri("avares://RimTransAI/Assets/no_picture.png"));
                PreviewImage = new Bitmap(assets);
            }
        }
        catch (Exception ex)
        {
            Services.Logger.Warning($"加载预览图失败: {ex.Message}");
            // 加载失败时尝试加载默认占位图
            try
            {
                var assets = AssetLoader.Open(new Uri("avares://RimTransAI/Assets/no_picture.png"));
                PreviewImage = new Bitmap(assets);
            }
            catch
            {
                PreviewImage = null;
            }
        }

        PackageId = modInfo.PackageId;
        Url = modInfo.Url;
        FolderPath = modInfo.ModFolderPath;

        // 加载支持版本
        SupportedVersions.Clear();
        foreach (var version in modInfo.SupportedVersions)
        {
            SupportedVersions.Add(version);
        }

        // 加载依赖关系
        Dependencies.Clear();
        foreach (var dependency in modInfo.ModDependencies)
        {
            Dependencies.Add(dependency);
        }

        // 通知属性变化
        OnPropertyChanged(nameof(HasUrl));
        OnPropertyChanged(nameof(HasSupportedVersions));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(DependenciesHeader));
    }

    /// <summary>
    /// 打开项目主页
    /// </summary>
    [RelayCommand]
    private void OpenUrl()
    {
        if (string.IsNullOrEmpty(Url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Services.Logger.Warning($"打开链接失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 创建固定尺寸的图片
    /// </summary>
    /// <param name="originalImage"></param>
    /// <returns></returns>
    public RenderTargetBitmap ResizeImage(Bitmap originalImage, int width, int height)
    {
        // 创建固定尺寸的图片
        var targetSize = new PixelSize(width, height);

        var renderTarget = new RenderTargetBitmap(targetSize, new Vector(96, 96));
        using (var drawingContext = renderTarget.CreateDrawingContext())
        {
            // 计算缩放以保持比例并填充
            var scaleX = (double)targetSize.Width / originalImage.PixelSize.Width;
            var scaleY = (double)targetSize.Height / originalImage.PixelSize.Height;
            var scale = Math.Max(scaleX, scaleY); // UniformToFill

            var scaledWidth = originalImage.PixelSize.Width * scale;
            var scaledHeight = originalImage.PixelSize.Height * scale;

            // 居中绘制
            var x = (targetSize.Width - scaledWidth) / 2;
            var y = (targetSize.Height - scaledHeight) / 2;

            drawingContext.DrawImage(originalImage,
                new Rect(0, 0, originalImage.PixelSize.Width, originalImage.PixelSize.Height),
                new Rect(x, y, scaledWidth, scaledHeight));
        }

        return renderTarget;
    }

    /// <summary>
    /// 打开 Mod 文件夹
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(FolderPath) || !System.IO.Directory.Exists(FolderPath))
        {
            Services.Logger.Warning("Mod 路径无效");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = FolderPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            Services.Logger.Warning($"打开文件夹失败: {ex.Message}");
        }
    }
}
