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

        if (value.Contains('/') || value.Contains('\\'))
        {
            return true;
        }

        var lower = value.ToLowerInvariant();
        foreach (var extension in PathLikeExtensions)
        {
            if (lower.EndsWith(extension, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public static class ExtractionReasonCodes
{
    public const string DefWhitelist = "Defs.Whitelist";

    public const string DefSmartSuffix = "Defs.SmartSuffix";

    public const string DefReflectionField = "Defs.ReflectionField";

    public const string DefListItem = "Defs.ListItem";

    public const string KeyedLeaf = "Keyed.Leaf";
}
