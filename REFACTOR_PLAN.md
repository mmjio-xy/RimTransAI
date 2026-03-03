# XML 扫描与字段提取彻底重构计划（对齐 RimWorld 加载逻辑）

## 0. 当前进度（2026-03-04）
- 阶段 0：已完成。
- 阶段 1：已完成。
- 阶段 2：已完成。
- 阶段 3：已完成（`DefsSourceParser`、`DefPathBuilder`、结构路径遍历与稳定性测试已落地）。
- 阶段 4：已完成（规则对象化、ReasonCode/SourceContext、冲突策略显式化、`ParseSingleFile` 新引擎切换与阶段测试已落地）。
- 阶段 5：已完成（主流程仅走 `ScanOrchestrator`，并补齐阶段化日志与去重/冲突/错误诊断）。
- 阶段 6：已完成（补齐稳定性与性能烟测、更新 README 扫描架构说明）。

---

## 1. 目标与约束

### 1.1 总目标
- 从 `XML 扫描 -> 可翻译字段提取 -> TranslationItem 产出` 全链路彻底重构。
- 尽量贴合 RimWorld 语言加载语义（`LoadFolders`、目录顺序、文件去重、覆盖规则、Keyed/DefInjected 解析方式）。
- 字段提取尽可能做到“高覆盖 + 可解释 + 可测试”。

### 1.2 强约束
- **不做新旧逻辑并行运行**。
- **不做差异对比模式**。
- 采用分阶段实施，但每阶段合入后都基于新架构继续推进。

### 1.3 成功标准（总体验收）
- 目录解析、文件去重、Keyed/DefInjected 加载规则与逆向代码语义一致。
- 关键测试集通过率 100%，且新增覆盖核心边界场景。
- 大型 Mod 扫描稳定（无随机结果、无明显性能退化）。

---

## 2. 总体重构范围

### 2.1 包含范围
- `RimTransAI/Services/ModParserService.cs`（核心扫描主流程重写）
- `RimTransAI/Services/TranslationExtractor.cs`（提取引擎重写）
- 新增 `Services/Scanning/*` 子模块（加载计划、文件注册、解析、提取、合并）
- 测试重建：`tests/RimTransAI.Tests/Services/*`（单元 + 集成）

### 2.2 不包含范围
- UI 功能改造（除非为扫描配置新增必要开关）
- 翻译 API 调用链改造（与本次扫描重构无关）
- 旧逻辑保留分支（本次不保留）

---

## 3. 目标架构（一次性迁移目标）

```
ScanOrchestrator
  -> GameLoadOrderPlanner
  -> LanguageDirectoryResolver
  -> FileRegistry (同 Mod 相对路径去重)
  -> XmlSourceCollector
  -> KeyedSourceParser / DefInjectedSourceParser / DefsSourceParser
  -> DefFieldExtractionEngine (反射驱动 + 路径构建)
  -> TranslationItemAssembler
```

### 3.1 核心语义对齐点
- `foldersToLoadDescendingOrder`：`LoadFolders.xml` 优先，含版本回退与条件判定。
- `IfModActive / IfModActiveAll / IfModNotActive`：严格按激活 Mod 集合判定。
- `CodeLinked` 与 `Keyed`：优先 `CodeLinked`，否则 `Keyed`。
- 同一 Mod 内按“相对路径”去重（先注册生效）。
- Key 冲突覆盖语义对齐 `SetOrAdd`（后写覆盖）。
- `\\n` 换行与 `TODO` 占位符语义对齐。

---

## 4. 分阶段实施计划（详细）

## 阶段 0：基线冻结与重构脚手架（0.5 天）
### 目标
- 为彻底替换做准备，建立新目录结构和测试骨架。

### 任务
- 建立 `Services/Scanning/` 子模块目录和接口骨架：
  - `GameLoadOrderPlanner`
  - `LanguageDirectoryResolver`
  - `FileRegistry`
  - `XmlSourceCollector`
  - `DefFieldExtractionEngine`
- 在测试工程新增对应测试文件骨架。
- 明确旧模块将被替换的入口点（`ModParserService.ScanModFolder`）。

### 产出
- 可编译的空实现 + 测试骨架。

### 验收
- `dotnet build` 通过。

---

## 阶段 1：加载目录与文件注册重构（1.5 天）
### 目标
- 完整替换目录解析逻辑，做到与游戏加载目录语义一致。

### 任务
- 实现 `GameLoadOrderPlanner`：
  - `LoadFolders.xml` 的版本选择顺序；
  - `default` 回退；
  - `当前版本目录 -> 兼容回退版本 -> Common -> Root` 回退策略。
- 实现条件判定：
  - `IfModActive`（任一）
  - `IfModActiveAll`（全部）
  - `IfModNotActive`（全部不激活）
- 实现 `FileRegistry`：
  - 同 Mod 相对路径去重；
  - 保证注册顺序确定性。

### 测试
- 新增测试场景：
  - 版本命中/回退；
  - 条件命中与不命中；
  - 同路径重复文件只保留首个。

### 产出
- 目录和文件加载计划由新模块统一输出。

### 验收
- 阶段 1 测试全部通过；
- 旧 `ResolveLoadFoldersForScan` 不再作为主路径。

---

## 阶段 2：语言文件扫描器重构（1.5 天）
### 目标
- 重构 Keyed/DefInjected/Strings/WordInfo 文件发现与解析入口。

### 任务
- 实现 `LanguageDirectoryResolver`：
  - 语言目录精确匹配 `folderName/legacyFolderName` 语义。
- Keyed 解析器重写：
  - `CodeLinked` 优先，否则 `Keyed`；
  - `LanguageData` 根节点要求；
  - `\\n` 转换、`TODO` 处理、重复 key 覆盖。
- DefInjected 文件发现重写：
  - 按 DefType 文件夹解析；
  - 支持 `DefLinked` 兼容路径警告语义。
- Strings/WordInfo 扫描纳入统一注册链（即使当前产出未直接使用，也保证语义完整）。

### 测试
- `KeyedParserTests` 全量改造；
- 新增 `LanguageDirectoryResolverTests`、`FileRegistryTests`。

### 验收
- Keyed 行为与逆向语义一致；
- 无旧 `ScanKeyedFiles` 主路径残留。

---

## 阶段 3：Defs 结构化遍历与路径构建重构（2 天）
### 目标
- 将 Defs 扫描从“启发式递归”升级为“结构化可追踪遍历”。

### 任务
- 重写 `DefsSourceParser`：
  - 根节点验证、异常隔离、稳定排序；
  - 输出结构化节点路径（含 `li` 索引路径）。
- 构建 `DefPathBuilder`：
  - 统一 key path 规范；
  - 与 DefInjected 路径模型对齐（`defName.field...`）。
- 建立循环保护和对象深度控制策略（避免异常 XML 造成爆炸遍历）。

### 测试
- 嵌套节点、列表节点、混合节点、异常 XML 的路径输出测试。

### 验收
- 路径生成稳定、可复现；
- Key 生成无随机性。

---

## 阶段 4：字段提取引擎重构（2.5 天）
### 目标
- 以反射为主、规则为辅，实现“尽可能全覆盖”的字段提取。

### 任务
- 重写 `DefFieldExtractionEngine`：
  - 反射字段优先（string / TaggedString / string 集合）；
  - 支持属性后备字段清洗；
  - `NoTranslate/MustTranslate` 属性语义。
- 规则层重构：
  - 黑名单、白名单、技术列表从硬编码迁移到可配置规则对象；
  - 内容特征过滤（资源路径/文件扩展名）保留；
  - 每条提取结果记录 `ReasonCode` 与来源上下文。
- 统一冲突处理：
  - 同 key 多来源覆盖策略显式化。

### 测试
- 反射命中、规则命中、冲突覆盖、列表提取、安全过滤全场景测试。

### 验收
- 提取链路完全由新引擎驱动；
- `TranslationExtractor` 旧实现移除或降级为薄封装。

---

## 阶段 5：主流程收口与旧逻辑清理（1 天）
### 目标
- 完成 `ModParserService` 主流程切换并移除旧分支。

### 任务
- `ModParserService.ScanModFolder` 改为仅调用新 `ScanOrchestrator`。
- 删除旧扫描私有方法与无效分支。
- 更新日志结构（阶段、文件数、去重数、提取数、错误数）。

### 测试
- 全量单元测试 + 集成测试。
- 典型大 Mod 样本稳定性验证（顺序和数量稳定）。

### 验收
- 代码中不再存在旧扫描主路径依赖；
- 构建与测试全绿。

---

## 阶段 6：质量封板（0.5 天）
### 目标
- 完成发布前质量闸门。

### 任务
- 测试覆盖检查与缺口补测。
- 性能回归检查（扫描耗时与内存峰值）。
- 更新 README/开发说明（新扫描架构与扩展方式）。

### 验收
- 发布级别质量通过。

---

## 5. 测试策略（必须执行）

### 5.1 单元测试
- 目录解析、条件判定、去重注册、路径构建、Keyed/DefInjected 解析。

### 5.2 集成测试
- 构造多目录多版本 Mod 样本，验证最终 `TranslationItem` 的稳定性。

### 5.3 稳定性测试
- 多次扫描同一输入，结果（顺序、数量、Key 集合）完全一致。

### 5.4 性能测试
- 至少两组大样本数据：记录总耗时、峰值内存、异常数。

---

## 6. 风险与应对

### 6.1 风险
- 彻底替换后，条目数量会变化（属于预期但影响感知明显）。
- 反射驱动提取可能引入性能压力。
- 历史特殊 Mod 目录结构导致边界异常。

### 6.2 应对
- 每阶段必须有可运行测试与验收门禁。
- 对扫描流程做分段计时与错误隔离，避免单文件拖垮全局。
- 规则对象化，快速调整过滤策略而不重写主链路。

---

## 7. 里程碑与时间预算（建议）
- 阶段 0-1：2 天
- 阶段 2-3：3.5 天
- 阶段 4：2.5 天
- 阶段 5-6：1.5 天
- **总计：约 9.5 天（可按资源压缩到 7-8 天）**

---

## 8. 执行原则
- 每阶段结束必须：
  - 代码可编译；
  - 相关测试通过；
  - 进入下一阶段前不留“半迁移”逻辑。
- 若阶段内发现与游戏语义冲突，以逆向代码行为为准修正实现。
