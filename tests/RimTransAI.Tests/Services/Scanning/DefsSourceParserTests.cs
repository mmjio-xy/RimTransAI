using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class DefsSourceParserTests
{
    [Fact]
    public void Parse_WithDefsRoot_BuildsStructuredPathsWithLiIndexes()
    {
        var root = CreateTempRoot();
        try
        {
            var file = Path.Combine(root, "ThingDefs.xml");
            File.WriteAllText(file, """
                <Defs>
                  <ThingDef>
                    <defName>TestGun</defName>
                    <label>Test Gun</label>
                    <rulesStrings>
                      <li>Rule A</li>
                      <li>Rule B</li>
                    </rulesStrings>
                  </ThingDef>
                </Defs>
                """);

            var parser = new DefsSourceParser();
            var result = parser.Parse(file);

            result.IsValidDefsRoot.Should().BeTrue();
            result.HitTraversalLimit.Should().BeFalse();
            result.Definitions.Should().ContainSingle();

            var nodes = Flatten(result.Definitions[0].Nodes);
            nodes.Should().Contain(x => x.Path == "label" && x.Value == "Test Gun");
            nodes.Should().Contain(x => x.Path == "rulesStrings.0" && x.Value == "Rule A");
            nodes.Should().Contain(x => x.Path == "rulesStrings.1" && x.Value == "Rule B");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_WithNonDefsRoot_ReturnsInvalidResult()
    {
        var root = CreateTempRoot();
        try
        {
            var file = Path.Combine(root, "Keyed.xml");
            File.WriteAllText(file, "<LanguageData><A>B</A></LanguageData>");

            var parser = new DefsSourceParser();
            var result = parser.Parse(file);

            result.IsValidDefsRoot.Should().BeFalse();
            result.Definitions.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_WithDepthLimit_SetsTraversalLimitFlag()
    {
        var root = CreateTempRoot();
        try
        {
            var file = Path.Combine(root, "Deep.xml");
            File.WriteAllText(file, """
                <Defs>
                  <ThingDef>
                    <defName>DeepNode</defName>
                    <a>
                      <b>
                        <c>
                          <d>TooDeep</d>
                        </c>
                      </b>
                    </a>
                  </ThingDef>
                </Defs>
                """);

            var parser = new DefsSourceParser(new DefsSourceParserOptions(MaxTraversalDepth: 2, MaxTraversalNodes: 50000));
            var result = parser.Parse(file);

            result.IsValidDefsRoot.Should().BeTrue();
            result.HitTraversalLimit.Should().BeTrue();

            var nodes = Flatten(result.Definitions[0].Nodes);
            nodes.Should().Contain(x => x.Path == "a.b.c");
            nodes.Should().NotContain(x => x.Path == "a.b.c.d");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Parse_SameInput_IsDeterministicAcrossRuns()
    {
        var root = CreateTempRoot();
        try
        {
            var file = Path.Combine(root, "ThingDefs.xml");
            File.WriteAllText(file, """
                <Defs>
                  <ThingDef>
                    <defName>A</defName>
                    <label>Alpha</label>
                  </ThingDef>
                  <ThingDef>
                    <defName>B</defName>
                    <rulesStrings>
                      <li>One</li>
                      <li>Two</li>
                    </rulesStrings>
                  </ThingDef>
                </Defs>
                """);

            var parser = new DefsSourceParser();
            var run1 = parser.Parse(file);
            var run2 = parser.Parse(file);

            var paths1 = run1.Definitions
                .SelectMany(x => Flatten(x.Nodes).Select(n => $"{x.DefName}:{n.Path}"))
                .ToList();
            var paths2 = run2.Definitions
                .SelectMany(x => Flatten(x.Nodes).Select(n => $"{x.DefName}:{n.Path}"))
                .ToList();

            paths1.Should().Equal(paths2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<ParsedDefNode> Flatten(IReadOnlyList<ParsedDefNode> nodes)
    {
        var result = new List<ParsedDefNode>();
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.Children.Count > 0)
            {
                result.AddRange(Flatten(node.Children));
            }
        }

        return result;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_defs_parser_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
