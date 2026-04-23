using AddMissingQuestRequirements.Pipeline.Weapon;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Weapon;

public class CaliberNormalizerTests
{
    // ── Mapping table coverage ───────────────────────────────────────────────

    [Theory]
    [InlineData("5.56x45",        "Caliber556x45NATO")]
    [InlineData("5.45x39",        "Caliber545x39")]
    [InlineData("7.62x39",        "Caliber762x39")]
    [InlineData("7.62x51",        "Caliber762x51")]
    [InlineData("7.62x54R",       "Caliber762x54R")]
    [InlineData("7.62x54 R",      "Caliber762x54R")]
    [InlineData("7.62x25",        "Caliber762x25TT")]
    [InlineData("7.62x25TT",      "Caliber762x25TT")]
    [InlineData("9x18",           "Caliber9x18PM")]
    [InlineData("9x19",           "Caliber9x19PARA")]
    [InlineData("9x21",           "Caliber9x21")]
    [InlineData("9x33R",          "Caliber9x33R")]
    [InlineData("9x39",           "Caliber9x39")]
    [InlineData("4.6x30",         "Caliber46x30")]
    [InlineData("5.7x28",         "Caliber57x28")]
    [InlineData("6.8x51",         "Caliber68x51")]
    [InlineData("12/70",          "Caliber12g")]
    [InlineData("12ga",           "Caliber12g")]
    [InlineData("20/70",          "Caliber20g")]
    [InlineData("20ga",           "Caliber20g")]
    [InlineData("23x75",          "Caliber23x75")]
    [InlineData("26x75",          "Caliber26x75")]
    [InlineData("30x29",          "Caliber30x29")]
    [InlineData("40x46",          "Caliber40x46")]
    [InlineData("40mmRU",         "Caliber40mmRU")]
    [InlineData(".366 TKM",       "Caliber366TKM")]
    [InlineData(".300 BLK",       "Caliber762x35")]
    [InlineData(".300 Blackout",  "Caliber762x35")]
    [InlineData(".338 LM",        "Caliber86x70")]
    [InlineData(".338 Lapua",     "Caliber86x70")]
    [InlineData(".45 ACP",        "Caliber1143x23ACP")]
    [InlineData(".50 BMG",        "Caliber127x99")]
    [InlineData("12.7x55",        "Caliber127x55")]
    [InlineData("12.7x33",        "Caliber127x33")]
    [InlineData(".725",           "Caliber725")]
    public void Known_display_strings_map_to_SPT_caliber_id(string input, string expected)
    {
        CaliberNormalizer.ToSpt(input).Should().Be(expected);
    }

    // ── Case-insensitivity ───────────────────────────────────────────────────

    [Theory]
    [InlineData("5.56X45",       "Caliber556x45NATO")]
    [InlineData("12GA",          "Caliber12g")]
    [InlineData(".300 blackout", "Caliber762x35")]
    public void Lookup_is_case_insensitive(string input, string expected)
    {
        CaliberNormalizer.ToSpt(input).Should().Be(expected);
    }

    // ── Pass-through: already in SPT CaliberXXX form ─────────────────────────

    [Theory]
    [InlineData("Caliber556x45NATO")]
    [InlineData("Caliber762x39")]
    [InlineData("Caliber9x19PARA")]
    public void Already_SPT_caliber_ids_pass_through_unchanged(string input)
    {
        CaliberNormalizer.ToSpt(input).Should().Be(input);
    }

    // ── Unknown token passes through unchanged ───────────────────────────────

    [Fact]
    public void Unknown_token_passes_through_unchanged()
    {
        CaliberNormalizer.ToSpt("SomeUnknownCaliber").Should().Be("SomeUnknownCaliber");
    }

    [Fact]
    public void Empty_string_passes_through_unchanged()
    {
        CaliberNormalizer.ToSpt(string.Empty).Should().Be(string.Empty);
    }
}
