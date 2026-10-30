using Shouldly;
using Xunit;

namespace DcLedger.Domain.Tests;

public sealed class LocationTests
{
    [Theory]
    [InlineData(36.0726, -79.792, true)] // Greensboro
    [InlineData(35.2271, -80.8431, true)] // Charlotte
    [InlineData(33.84, -84.33, true)] // south-west corner, inclusive
    [InlineData(36.59, -75.46, true)] // north-east corner, inclusive
    [InlineData(33.8399, -80.0, false)]
    [InlineData(36.5901, -80.0, false)]
    [InlineData(35.0, -84.3301, false)]
    [InlineData(35.0, -75.4599, false)]
    [InlineData(-79.792, 36.0726, false)] // swapped
    public void Only_points_inside_north_carolinas_box_are_in_north_carolina(double latitude, double longitude, bool inside)
    {
        new Location { Latitude = latitude, Longitude = longitude }.IsInNorthCarolina.ShouldBe(inside);
    }
}
