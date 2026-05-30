# RimTransAI — 边缘世界智能汉化工具

RimTransAI 是一个用于 RimWorld Mod 汉化的桌面应用。它会扫描 Mod 内容、提取可翻译文本、调用兼容 OpenAI 格式的 LLM 接口进行批量翻译，并生成标准 `Languages` 目录结构。

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 | .NET 9 |
| UI 框架 | Avalonia 12 + Semi.Avalonia |
| 图标 | Material.Icons.Avalonia |
| MVVM | CommunityToolkit.Mvvm |
| LLM 客户端 | OpenAI .NET SDK 2.x |
| 反射分析 | Mono.Cecil |
| DI | Microsoft.Extensions.DependencyInjection |
| 测试 | xUnit + FluentAssertions + NSubstitute + coverlet |

## 当前特性

- 三栏工作台 UI：左栏 Mod 信息、中栏 Mod 列表、右栏翻译条目表格
- Mod 信息面板：预览图、作者、简介（支持富文本）、包 ID、项目主页
- 多来源管理：表格维护来源目录，支持图标、启用状态、增删改
- 翻译条目手动加载，支持按版本过滤
- **译文弹出编辑**：点击 ✏️ 图标或双击译文格子弹出专用编辑窗口，自动标记已翻译状态
- **智能分批翻译**：基于 Token 估算自动分批，支持单线程/多线程模式
- **OpenAI SDK 集成**：使用官方 SDK 发送请求，自动处理序列化与错误
- **API 地址自动补全开关**：默认自动补全 `/v1/chat/completions`，可关闭以兼容非标准代理
- 翻译结果输出为 `Languages/<语言>/Keyed|DefInjected|Backstories` 标准结构
- 备份管理：自动备份（保存时触发）、列表查询、SHA-256 校验、按数量清理、恢复

## 环境要求

- .NET 9 SDK
- Windows（当前主要验证平台）

## 快速开始

```bash
# 还原（首次或依赖变更后）
dotnet restore RimTransAI.sln --force-evaluate

# 构建
dotnet build RimTransAI.sln -c Release

# 运行
dotnet run --project RimTransAI/RimTransAI.csproj

# 测试
dotnet test tests/RimTransAI.Tests/RimTransAI.Tests.csproj -c Release
```

## 使用流程

1. 打开"程序设置"：
   - **API 设置**：填写 API 地址、Key、模型名称；可按需关闭地址自动补全
   - **Mod 来源**：添加一个或多个 Mod 来源目录
   - **备份设置**：配置自动备份、存储目录、数量上限
   - **多线程设置**：启用多线程翻译并调整并发参数
2. 在中栏选择 Mod，支持按名称或包名搜索。
3. 在右栏点击"加载翻译条目"（已缓存时显示"刷新翻译条目"）。
4. 可按版本过滤后点击"开始翻译"；双击译文格子或点击 ✏️ 图标可手动编辑。
5. 翻译完成后点击"保存"，自动生成 `Languages` 目录结构并触发备份。

## 项目结构

```text
RimTransAI/
  Models/              数据模型（AppConfig, TranslationItem, ModInfo 等）
  Services/            核心服务
    Scanning/          扫描子系统（目录规划→源文件收集→字段提取）
  ViewModels/          MVVM ViewModel 层
  Views/               Avalonia 视图（含 TranslationEditWindow 弹窗）
  Converters/          值转换器（日志颜色、布尔取反等）
  Assets/              静态资源（图标、预览图）
tests/RimTransAI.Tests/
  Services/            服务层测试
  Services/Scanning/   扫描子系统专项测试
  Models/              模型测试
  Helpers/             测试辅助类
  TestData/            样本 XML 与配置
```

## 许可证

本项目采用 [GNU GPL v3.0](LICENSE)。

## 国内下载地址

<https://mmjio.lanzoue.com/b01yory9ij>
密码: `i4z8`
