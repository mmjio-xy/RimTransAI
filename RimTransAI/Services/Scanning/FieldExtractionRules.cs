using System;
using System.Collections.Generic;
using System.Linq;

namespace RimTransAI.Services.Scanning;

public sealed class FieldExtractionRuleSet
{
    public HashSet<string> BlacklistFields { get; }

    public string[] BlacklistKeywords { get; }

    public HashSet<string> TechnicalListFields { get; }

    public HashSet<string> WhitelistFields { get; }

    public HashSet<string> SafeTextLists { get; }

    public string[] SmartSuffixes { get; }

    public string[] PathLikeExtensions { get; }

    public FieldExtractionRuleSet(
        IEnumerable<string> blacklistFields,
        IEnumerable<string> blacklistKeywords,
        IEnumerable<string> technicalListFields,
        IEnumerable<string> whitelistFields,
        IEnumerable<string> safeTextLists,
        IEnumerable<string> smartSuffixes,
        IEnumerable<string> pathLikeExtensions)
    {
        BlacklistFields = new HashSet<string>(blacklistFields ?? throw new ArgumentNullException(nameof(blacklistFields)), StringComparer.OrdinalIgnoreCase);
        BlacklistKeywords = (blacklistKeywords ?? throw new ArgumentNullException(nameof(blacklistKeywords))).ToArray();
        TechnicalListFields = new HashSet<string>(technicalListFields ?? throw new ArgumentNullException(nameof(technicalListFields)), StringComparer.OrdinalIgnoreCase);
        WhitelistFields = new HashSet<string>(whitelistFields ?? throw new ArgumentNullException(nameof(whitelistFields)), StringComparer.OrdinalIgnoreCase);
        SafeTextLists = new HashSet<string>(safeTextLists ?? throw new ArgumentNullException(nameof(safeTextLists)), StringComparer.OrdinalIgnoreCase);
        SmartSuffixes = (smartSuffixes ?? throw new ArgumentNullException(nameof(smartSuffixes))).ToArray();
        PathLikeExtensions = (pathLikeExtensions ?? throw new ArgumentNullException(nameof(pathLikeExtensions))).ToArray();
    }

    public static FieldExtractionRuleSet CreateDefault()
    {
        return new FieldExtractionRuleSet(
            blacklistFields:
            [
                "defname", "def",
                "texpath", "texture", "texturepath", "iconpath", "shader",
                "sound", "sounddef", "soundcast", "soundhit", "soundmelee",
                "tag", "tags", "class", "workerclass", "driverclass", "linktype", "drawertype",
                "pathcost", "altitudelayer", "graphicdata",
                "path", "file", "icon", "graphic",
                "setting", "config", "worker", "link", "curve",
                "size", "color", "mask", "effect", "cost", "stat",
                "tex", "coordinates", "offset",
                "key"
            ],
            blacklistKeywords:
            [
                "defname",
                "path", "texpath", "texture", "iconpath", "shader",
                "sound",
                "tag", "class", "worker", "driver", "drawer", "link",
                "pathcost", "altitudelayer", "graphic",
                "file", "icon", "graphic", "setting", "config", "link", "curve",
                "size", "color", "mask", "effect", "cost", "stat",
                "tex", "coordinates", "offset"
            ],
            technicalListFields:
            [
                "prerequisites", "researchPrerequisites", "hiddenPrerequisites",
                "requiredResearchFacilities", "requiredMemeList",
                "thingCategories", "stuffCategories", "tradeTags", "weaponTags", "apparelTags", "destroyOnDrop",
                "categories", "filter", "fixedIngredientFilter", "defaultIngredientFilter", "ingredients", "products",
                "specialProducts", "backstoryFiltersOverride",
                "recipeUsers", "recipes", "importRecipesFrom", "excludeOres",
                "workTypes", "roleTags",
                "appliedOnFixedBodyParts", "groups", "hediffGivers",
                "requiredCapacities", "affordances", "capabilities", "statBases", "equippedStatOffsets",
                "capacities",
                "inspectorTabs", "compClass", "comps", "modExtensions"
            ],
            whitelistFields:
            [
                "label", "description",
                "jobstring", "verb", "labelnoun", "pawnlabel",
                "reportstring", "baseinspectline", "deathmessage",
                "text", "lettertext", "letterlabel",
                "rulesstrings", "slateref",
                "questnamerules", "questdescriptionrules",
                "labelshort", "gerund", "message", "labelkey"
            ],
            safeTextLists:
            [
                "rulesstrings", "descriptions", "labels", "messages", "helptexts", "baseinspectline"
            ],
            smartSuffixes:
            [
                "label", "text", "desc", "message", "string"
            ],
            pathLikeExtensions:
            [
                ".png", ".jpg", ".jpeg", ".gif", ".wav", ".mp3", ".xml", ".txt"
            ]);
    }

    public bool IsBlacklisted(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return true;
        }

        if (BlacklistFields.Contains(fieldName))
        {
            return true;
        }

        var span = fieldName.AsSpan();
        foreach (var keyword in BlacklistKeywords)
        {
            if (span.Contains(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTechnicalList(string fieldName)
    {
        return TechnicalListFields.Contains(fieldName);
    }

    public bool IsWhitelistedField(string fieldName)
    {
        return WhitelistFields.Contains(fieldName);
    }

    public bool IsSafeTextList(string fieldName)
    {
        return SafeTextLists.Contains(fieldName);
    }

    public bool IsSmartSuffixMatch(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var lower = fieldName.ToLowerInvariant();
        foreach (var suffix in SmartSuffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsPathLikeContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        var lower = trimmed.ToLowerInvariant();
        foreach (var extension in PathLikeExtensions)
        {
            if (lower.EndsWith(extension, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!trimmed.Contains('/') && !trimmed.Contains('\\'))
        {
            return false;
        }

        if (EndsWithSentencePunctuation(trimmed))
        {
            return false;
        }

        if (trimmed.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        var segments = trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsPathSegmentLike(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EndsWithSentencePunctuation(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var last = value[^1];
        return last is '.' or ',' or '!' or '?' or ';' or '。' or '，' or '！' or '？' or '；';
    }

    private static bool IsPathSegmentLike(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        foreach (var ch in segment)
        {
            if (char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (ch is '_' or '-' or '.' or ':')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

public static class ExtractionReasonCodes
{
    public const string DefWhitelist = "Defs.Whitelist";

    public const string DefSmartSuffix = "Defs.SmartSuffix";

    public const string DefReflectionField = "Defs.ReflectionField";

    public const string DefListItem = "Defs.ListItem";

    public const string DefInjectedLeaf = "DefInjected.Leaf";

    public const string DefInjectedListItem = "DefInjected.ListItem";

    public const string KeyedLeaf = "Keyed.Leaf";
}
