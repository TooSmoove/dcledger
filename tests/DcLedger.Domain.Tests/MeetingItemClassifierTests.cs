using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class MeetingItemClassifierTests
{
    [Theory]
    [InlineData("Public Hearing on a 180-Day Moratorium on Data Centers in Unincorporated Areas", "data_center")]
    [InlineData("Text Amendment Adding Data Processing Facility as a Use", "data_center")]
    [InlineData("Datacenter Campus Annexation", "datacenter")]
    [InlineData("DATA-CENTRE zoning standards", "data_center")]
    [InlineData("Hyper-scale campus site plan", "hyperscale")]
    [InlineData("Proposed server farm on Elm Road", "server_farm")]
    [InlineData("Co-location facility incentive", "colocation")]
    public void Data_center_items_are_candidates(string title, string expectedRule)
    {
        var result = MeetingItemClassifier.Classify(title);

        result.IsCandidate.ShouldBeTrue();
        result.MatchedRules.ShouldContain(expectedRule);
    }

    [Theory]
    [InlineData("Approval of Minutes")]
    [InlineData("Ordinance Amending the UDO: Text Amendment for Accessory Dwellings")]
    [InlineData("Zoning Petition from RS9 to LI (Light Industrial)")]
    [InlineData("Economic Development Incentive Agreement")]
    [InlineData("Update on the Data Collection Center Renovation")]
    [InlineData("Data-driven budget priorities")]
    [InlineData("Capital Project Ordinance for the Senior Center")]
    public void Items_without_data_center_terms_are_not_candidates(string title)
    {
        var result = MeetingItemClassifier.Classify(title);

        result.IsCandidate.ShouldBeFalse();
        result.MatchedRules.ShouldBeEmpty();
    }

    [Fact]
    public void Every_rule_name_is_listed()
    {
        MeetingItemClassifier.RuleNames.Count.ShouldBe(5);
        MeetingItemClassifier.RuleNames.ShouldBeUnique();
    }
}
