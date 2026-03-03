# RimTransAI - 边缘世界智能汉化工具

RimTransAI 是一个用于 RimWorld Mod 汉化的桌面应用。它会扫描 Mod 内容、提取可翻译文本、调用兼容 OpenAI 格式的 LLM 接口进行批量翻译，并生成标准 `Languages` 目录结构。

## 当前特性

- 三栏工作台 UI：
  - 左栏：Mod 信息（预览图、作者、简介、包ID、目录与项目主页）
  - 中栏：Mod 列表（支持按 Mod 名称 / 包名搜索）
  - 右栏：翻译条目表格与版本过滤
- 翻译条目手动加载：未缓存时显示“加载翻译条目”，有缓存时显示“刷新翻译条目”。
- 多来源管理：在“程序设置 > Mod 来源”中以表格维护来源目录，支持图标、启用状态、增删改。
- 简介富文本渲染：支持 `<b>`、`<color=...>`（含 `&lt;...&gt;` 转义文本）及多行显示。
- 项目主页解析：
  - 优先读取 `About/About.xml` 中的 `<url>`。
  - 若为空，且当前 Mod 文件夹名为纯数字，则自动拼接 `https://steamcommunity.com/sharedfiles/filedetails/?id={folderName}`。
  - 否则显示“无项目主页”。
- 备份管理：支持自动备份、列表查询、恢复。

## 环境要求

- .NET 9 SDK（开发/本地构建）
- Windows（当前主要验证平台）

## 快速开始

```bash
# 构建
dotnet build RimTransAI.sln -c Release

# 运行
dotnet run --project RimTransAI/RimTransAI.csproj

# 测试
dotnet test tests/RimTransAI.Tests/RimTransAI.Tests.csproj -c Release
```

## 使用流程

1. 打开“程序设置”，配置 API 与 Mod 来源目录。
2. 在中栏选择 Mod，可通过下拉框切换搜索模式（按 Mod 名 / 按包名）并输入关键词筛选。
3. 在右栏点击“加载翻译条目”（或“刷新翻译条目”）。
4. 可按版本过滤后执行翻译，完成后点击“保存”。

## 配置文件

配置文件为 `settings.json`（与程序同目录），常用字段包括：

- `ApiUrl` / `ApiKey` / `TargetModel`
- `TargetLanguage`
- `ModSourceFolders`
- `EnableMultiThreadTranslation` / `MaxThreads`
- `EnableAutoBackup` / `MaxBackupCount`

## 项目结构

```text
RimTransAI/
  Models/
  Services/
  ViewModels/
  Views/
  Assets/
tests/RimTransAI.Tests/
```

## 扫描架构（重构后）

当前扫描链路由 `ScanOrchestrator` 统一编排：

1. `GameLoadOrderPlanner`：根据 `LoadFolders.xml` 与版本回退规则规划加载目录。
2. `LanguageDirectoryResolver`：解析语言目录（含 legacy 名称回退）。
3. `XmlSourceCollector` + `FileRegistry`：收集 `Defs/Keyed/DefInjected/Strings/WordInfo` 并做同作用域去重。
4. `DefFieldExtractionEngine`：按结构化节点遍历提取字段，输出 `TranslationItem`，并记录提取原因与上下文。

关键扩展点：

- 规则配置：`Services/Scanning/FieldExtractionRules.cs`
- Defs 结构解析：`Services/Scanning/DefsSourceParser.cs`
- 路径规范化：`Services/Scanning/DefPathBuilder.cs`
- 提取冲突策略：`ExtractionConflictPolicy`（`LastWriteWins` / `FirstWriteWins`）

扫描诊断信息（`ScanDiagnostics`）包含：

- 目录与文件计数（LoadFolders/LanguageDirs/Defs/Keyed/DefInjected/Strings/WordInfo）
- 源文件尝试/注册/去重统计
- 提取结果统计（提取条目数、冲突数、错误数）

## 许可证

本项目采用 [GNU GPL v3.0](LICENSE)。
