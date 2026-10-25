using Shouldly;
using Xunit;

namespace DcLedger.Architecture.Tests;

public sealed class DependencyRulesTests
{
    public static TheoryData<string> SourceProjects()
    {
        var data = new TheoryData<string>();
        foreach (var project in SourceProjectFiles().Keys.Order(StringComparer.Ordinal))
        {
            data.Add(project);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SourceProjects))]
    public void Source_project_follows_the_dependency_rules(string projectName)
    {
        var xml = File.ReadAllText(SourceProjectFiles()[projectName]);

        DependencyRules.Violations(projectName, xml).ShouldBeEmpty();
    }

    [Fact]
    public void Every_rule_has_a_matching_source_project()
    {
        SourceProjectFiles().Keys.Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(DependencyRules.AllowedProjects.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_forbidden_project_reference_is_reported()
    {
        const string xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\DcLedger.Infrastructure\DcLedger.Infrastructure.csproj" />
              </ItemGroup>
            </Project>
            """;

        DependencyRules.Violations("DcLedger.Domain", xml)
            .ShouldHaveSingleItem().ShouldBe("DcLedger.Domain must not reference project DcLedger.Infrastructure.");
    }

    [Fact]
    public void A_forbidden_package_reference_is_reported()
    {
        const string xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="AWSSDK.S3" />
              </ItemGroup>
            </Project>
            """;

        DependencyRules.Violations("DcLedger.Application", xml)
            .ShouldHaveSingleItem().ShouldBe("DcLedger.Application must not reference package AWSSDK.S3.");
    }

    [Fact]
    public void An_unknown_project_is_reported()
    {
        DependencyRules.Violations("DcLedger.Unplanned", "<Project />")
            .ShouldHaveSingleItem().ShouldBe("DcLedger.Unplanned has no entry in DependencyRules; add one deliberately.");
    }

    private static Dictionary<string, string> SourceProjectFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DcLedger.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("DcLedger.slnx not found above " + AppContext.BaseDirectory);
    }
}
