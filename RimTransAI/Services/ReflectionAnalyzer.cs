using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace RimTransAI.Services;

/// <summary>
/// 使用 Mono.Cecil 分析 RimWorld 核心程序集和 Mod 程序集
/// 模拟 RimWorld 游戏加载 Mod 的过程，构建完整的类型继承树
/// </summary>
public class ReflectionAnalyzer
{
    // 需要关注的核心基类名称
    private static readonly string[] CoreBaseTypeNames =
    {
        "Verse.Def",
        "Verse.CompProperties"
    };

    // 类型缓存：完整类名 -> TypeDefinition
    private readonly Dictionary<string, TypeDefinition> _typeCache = new();

    // 类型继承映射：类名 -> 可翻译字段集合
    private readonly Dictionary<string, HashSet<string>> _typeFieldsMap = new();

    // 核心程序集
    private AssemblyDefinition? _coreAssembly;

    // 性能优化：TypeReference 缓存，避免重复创建对象
    private readonly Dictionary<string, TypeReference> _typeReferenceCache = new();

    /// <summary>
    /// 加载核心程序集 (Assembly-CSharp.dll)
    /// 模拟 RimWorld 启动时加载核心类型的过程
    /// </summary>
    public void LoadCore(string corePath)
    {
        if (!File.Exists(corePath))
        {
            Logger.Error($"核心程序集不存在: {corePath}");
            throw new FileNotFoundException($"核心程序集不存在: {corePath}");
        }

        // [性能优化] 清理旧的 TypeReference 缓存
        _typeReferenceCache.Clear();

        try
        {
            Logger.Info($"========== 开始加载核心程序集 ==========");
            Logger.Info($"路径: {corePath}");

            // 创建自定义的程序集解析器
            var resolver = new DefaultAssemblyResolver();

            // 添加核心程序集所在目录到搜索路径
            string? coreDir = Path.GetDirectoryName(corePath);
            if (!string.IsNullOrEmpty(coreDir))
            {
                resolver.AddSearchDirectory(coreDir);
            }

            // 读取核心程序集
            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Deferred
            };

            _coreAssembly = AssemblyDefinition.ReadAssembly(corePath, readerParameters);

            // 第一步：构建核心类型缓存
            Logger.Info("正在构建核心类型缓存...");
            BuildCoreTypeCache();

            // 第二步：分析核心基类及其派生类的可翻译字段
            Logger.Info("正在分析核心类型的可翻译字段...");
            AnalyzeCoreTypes();

            Logger.Info($"========== 核心程序集加载完成 ==========");
            Logger.Info($"类型缓存: {_typeCache.Count} 个类型");
            Logger.Info($"字段映射: {_typeFieldsMap.Count} 个类型");
        }
        catch (Exception ex)
        {
            Logger.Error("加载核心程序集失败", ex);
            throw new InvalidOperationException($"加载核心程序集失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 构建核心类型缓存
    /// 模拟 RimWorld 加载所有核心类型到内存
    /// </summary>
    private void BuildCoreTypeCache()
    {
        if (_coreAssembly == null) return;

        _typeCache.Clear();

        // 遍历所有类型并缓存
        foreach (var type in _coreAssembly.MainModule.Types)
        {
            CacheTypeRecursive(type);
        }

        Logger.Info($"已缓存 {_typeCache.Count} 个核心类型");
    }

    /// <summary>
    /// 递归缓存类型（包括嵌套类型）
    /// </summary>
    private void CacheTypeRecursive(TypeDefinition type)
    {
        if (!_typeCache.ContainsKey(type.FullName))
        {
            _typeCache[type.FullName] = type;
        }

        // 递归处理嵌套类型
        foreach (var nestedType in type.NestedTypes)
        {
            CacheTypeRecursive(nestedType);
        }
    }

    /// <summary>
    /// 分析核心类型的可翻译字段
    /// 模拟 RimWorld 分析 Def 和 CompProperties 的字段结构
    /// </summary>
    private void AnalyzeCoreTypes()
    {
        _typeFieldsMap.Clear();

        // 分析所有核心基类及其派生类
        foreach (var baseTypeName in CoreBaseTypeNames)
        {
            if (_typeCache.TryGetValue(baseTypeName, out var baseType))
            {
                Logger.Info($"分析基类: {baseTypeName}");
                AnalyzeTypeHierarchy(baseType);
            }
        }

        Logger.Info($"已分析 {_typeFieldsMap.Count} 个类型的字段");
    }

    /// <summary>
    /// 分析类型层次结构（包括所有派生类）
    /// </summary>
    private void AnalyzeTypeHierarchy(TypeDefinition baseType)
    {
        // 分析基类本身
        AnalyzeTypeFields(baseType);

        // 查找所有派生类
        foreach (var type in _typeCache.Values)
        {
            if (IsInheritFrom(type, baseType.FullName))
            {
                AnalyzeTypeFields(type);
            }
        }
    }

    /// <summary>
    /// 分析单个类型的可翻译字段
    /// </summary>
    private void AnalyzeTypeFields(TypeDefinition type)
    {
        if (_typeFieldsMap.ContainsKey(type.FullName))
        {
            return; // 已分析过
        }

        var translatableFields = new HashSet<string>();

        // 收集当前类型的所有可翻译字段
        CollectTranslatableFields(type, translatableFields);

        // 递归收集基类的可翻译字段
        var currentType = type;
        while (currentType?.BaseType != null)
        {
            try
            {
                currentType = ResolveType(currentType.BaseType.FullName);
                if (currentType != null)
                {
                    CollectTranslatableFields(currentType, translatableFields);
                }
            }
            catch
            {
                break;
            }
        }

        if (translatableFields.Count > 0)
        {
            _typeFieldsMap[type.FullName] = translatableFields;
            Logger.Debug($"  类型 {type.Name}: {translatableFields.Count} 个字段");
        }
    }

    /// <summary>
    /// 收集类型的可翻译字段
    /// </summary>
    private void CollectTranslatableFields(TypeDefinition type, HashSet<string> fields)
    {
        foreach (var field in type.Fields)
        {
            if (IsTranslatableField(field))
            {
                // 如果是后台字段，提取真实属性名
                string finalName = field.Name;
                if (field.Name.Contains("<") && field.Name.Contains(">"))
                {
                    finalName = CleanBackingFieldName(field.Name);
                }
            
                fields.Add(finalName);
            }
        }
    }

    /// <summary>
    /// 分析 Mod 程序集，提取继承自核心基类的类型及其可翻译字段
    /// 模拟 RimWorld 加载 Mod 的过程
    /// </summary>
    public Dictionary<string, HashSet<string>> AnalyzeModAssembly(string modDllPath)
    {
        if (_coreAssembly == null || _typeCache.Count == 0)
        {
            throw new InvalidOperationException("必须先调用 LoadCore 加载核心程序集");
        }

        if (!File.Exists(modDllPath))
        {
            throw new FileNotFoundException($"Mod 程序集不存在: {modDllPath}");
        }

        Logger.Info($"========== 分析 Mod 程序集 ==========");
        Logger.Info($"路径: {modDllPath}");

        var result = new Dictionary<string, HashSet<string>>();

        try
        {
            // 创建自定义的程序集解析器
            var resolver = new CustomAssemblyResolver();

            // 添加 Mod DLL 所在目录到搜索路径
            string? modDir = Path.GetDirectoryName(modDllPath);
            if (!string.IsNullOrEmpty(modDir))
            {
                resolver.AddSearchDirectory(modDir);
            }

            // 添加核心程序集所在目录到搜索路径
            if (_coreAssembly.MainModule.FileName != null)
            {
                string? coreDir = Path.GetDirectoryName(_coreAssembly.MainModule.FileName);
                if (!string.IsNullOrEmpty(coreDir))
                {
                    resolver.AddSearchDirectory(coreDir);
                }
            }

            // 读取 Mod 程序集
            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Deferred
            };

            using var modAssembly = AssemblyDefinition.ReadAssembly(modDllPath, readerParameters);

            // 第一步：缓存 Mod 程序集中的所有类型
            Logger.Info("正在缓存 Mod 类型...");
            foreach (var type in modAssembly.MainModule.Types)
            {
                CacheTypeRecursive(type);
            }

            // 第二步：分析 Mod 中继承自核心基类的类型
            Logger.Info("正在分析 Mod 类型...");
            foreach (var type in modAssembly.MainModule.Types)
            {
                AnalyzeModType(type, result);
            }

            Logger.Info($"========== Mod 程序集分析完成 ==========");
            Logger.Info($"找到 {result.Count} 个可翻译类型");
        }
        catch (Exception ex)
        {
            Logger.Error($"分析 Mod 程序集时出错", ex);
        }

        return result;
    }

    /// <summary>
    /// 获取所有已加载的类型字段映射
    /// 返回当前缓存的所有类型字段映射（包含 Core 和 Mod）
    /// </summary>
    /// <returns>类型名 -> 可翻译字段集合的映射</returns>
    public Dictionary<string, HashSet<string>> GetAllTypeFields()
    {
        // 返回当前缓存的所有类型字段映射（Core + Mod）
        return new Dictionary<string, HashSet<string>>(_typeFieldsMap);
    }

    /// <summary>
    /// 分析 Mod 类型（递归处理嵌套类型）
    /// </summary>
    private void AnalyzeModType(TypeDefinition type, Dictionary<string, HashSet<string>> result)
    {
        // 跳过编译器生成的类型
        if (type.Name.Contains("<"))
        {
            return;
        }

        // 检查是否继承自核心基类
        foreach (var baseTypeName in CoreBaseTypeNames)
        {
            if (IsInheritFrom(type, baseTypeName))
            {
                // 分析该类型的字段
                AnalyzeTypeFields(type);

                // 如果有可翻译字段，添加到结果中
                if (_typeFieldsMap.TryGetValue(type.FullName, out var fields))
                {
                    result[type.FullName] = fields;
                    Logger.Debug($"  找到类型: {type.Name}, 字段数: {fields.Count}");
                }

                break;
            }
        }

        // 递归处理嵌套类型
        foreach (var nestedType in type.NestedTypes)
        {
            AnalyzeModType(nestedType, result);
        }
    }

    /// <summary>
    /// 检查类型是否继承自指定基类（支持多级继承）
    /// </summary>
    private bool IsInheritFrom(TypeDefinition type, string baseTypeName)
    {
        if (type.BaseType == null)
        {
            return false;
        }

        var current = type;
        var visited = new HashSet<string>();

        while (current?.BaseType != null)
        {
            var currentBaseTypeName = current.BaseType.FullName;

            // 防止无限循环
            if (!visited.Add(currentBaseTypeName))
            {
                break;
            }

            // 检查是否匹配目标基类
            if (currentBaseTypeName == baseTypeName)
            {
                return true;
            }

            // 尝试解析基类定义
            current = ResolveType(currentBaseTypeName);
            if (current == null)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析类型（优先从缓存中查找）
    /// </summary>
    private TypeDefinition? ResolveType(string fullTypeName)
    {
        // 优先从缓存中查找
        if (_typeCache.TryGetValue(fullTypeName, out var cachedType))
        {
            return cachedType;
        }

        // 尝试从核心程序集解析
        try
        {
            // [性能优化] 缓存 TypeReference，避免重复创建
            if (!_typeReferenceCache.TryGetValue(fullTypeName, out var typeRef))
            {
                typeRef = new TypeReference("", fullTypeName, _coreAssembly?.MainModule, _coreAssembly?.MainModule);
                _typeReferenceCache[fullTypeName] = typeRef;
            }
            return typeRef.Resolve();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查类型是否为字符串集合（List<string> 或 IEnumerable<string> 或 List<TaggedString>）
    /// </summary>
    private bool IsStringCollectionType(TypeReference typeRef)
    {
        // 检查是否为泛型实例类型
        if (typeRef is not GenericInstanceType genericType)
        {
            return false;
        }

        // 检查泛型参数是否为 System.String 或 Verse.TaggedString
        if (genericType.GenericArguments.Count != 1)
        {
            return false;
        }

        var genericArgType = genericType.GenericArguments[0].FullName;
        if (genericArgType != "System.String" && genericArgType != "Verse.TaggedString")
        {
            return false;
        }

        // 检查基类型是否为 List 或 IEnumerable
        var elementType = genericType.ElementType;
        var elementTypeName = elementType.FullName;

        // 支持的集合类型
        return elementTypeName == "System.Collections.Generic.List`1" ||
               elementTypeName == "System.Collections.Generic.IEnumerable`1" ||
               elementTypeName == "System.Collections.Generic.IList`1" ||
               elementTypeName == "System.Collections.Generic.ICollection`1";
    }

    /// <summary>
    /// 判断字段是否可翻译
    /// 规则：
    /// 1. 带有 [MustTranslate] 特性的字段必须翻译
    /// 2. 类型为 string 或 TaggedString 的公共字段
    /// 3. 类型为 List<string> 或 IEnumerable<string> 或 List<TaggedString> 的字段
    /// 4. 黑名单过滤：排除内部 ID、配置项、资源引用等非翻译字段
    ///    - 路径/文件: path, file, tex, texture
    ///    - ID/标签: defname, tag, icon
    ///    - 配置: setting, config, class, list
    ///    - 资源: graphic, sound, effect, mask
    ///    - 逻辑: cannot, must, whitelist, blacklist
    /// 5. 跳过静态字段和编译器生成的字段
    /// </summary>
    private bool IsTranslatableField(FieldDefinition field)
    {
        if (field.IsStatic) return false;

        //  允许自动属性的后台字段
        // 只有当名字包含 < 但不包含 BackingField 时，才认为是编译器生成的垃圾并过滤
        bool isBackingField = field.Name.Contains("<") && field.Name.Contains("BackingField");
        if (field.Name.Contains("<") && !isBackingField) return false;

        // 性能优化：不执行 ToLower()，直接使用 CompareTo 检查

        // 检查 MustTranslate 特性
        if (field.CustomAttributes.Any(attr => attr.AttributeType.Name == "MustTranslateAttribute")) return true;

        // 检查 string 或 TaggedString
        var fieldTypeName = field.FieldType.FullName;
        if (fieldTypeName == "System.String" || fieldTypeName == "Verse.TaggedString") return true;

        // 检查集合
        if (IsStringCollectionType(field.FieldType)) return true;

        return false;
    }

    // [新增] 清理后台字段名称
    private string CleanBackingFieldName(string fieldName)
    {
        // 将 <Label>k__BackingField 转换为 Label
        int start = fieldName.IndexOf('<');
        int end = fieldName.IndexOf('>');
        if (start >= 0 && end > start)
        {
            return fieldName.Substring(start + 1, end - start - 1);
        }

        return fieldName;
    }
}

/// <summary>
/// 自定义程序集解析器，处理依赖解析失败的情况
/// </summary>
internal class CustomAssemblyResolver : DefaultAssemblyResolver
{
    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        try
        {
            return base.Resolve(name);
        }
        catch (AssemblyResolutionException)
        {
            // 依赖解析失败时返回 null，而不是抛出异常
            // 这样可以继续分析其他部分
            return null!;
        }
    }
}
