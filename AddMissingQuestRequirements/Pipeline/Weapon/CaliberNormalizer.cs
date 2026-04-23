namespace AddMissingQuestRequirements.Pipeline.Weapon;

/// <summary>
/// Translates human-readable caliber display strings (e.g. <c>"5.56x45"</c>) to the SPT
/// <c>ammoCaliber</c> ID format (e.g. <c>"Caliber556x45NATO"</c>).
///
/// Tokens that are already in <c>CaliberXXX</c> form, or are otherwise unknown, pass through
/// unchanged so that callers can always use the output for direct ID comparison.
/// </summary>
public static class CaliberNormalizer
{
    private static readonly Dictionary<string, string> DisplayToSpt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["5.56x45"]       = "Caliber556x45NATO",
            ["5.45x39"]       = "Caliber545x39",
            ["7.62x39"]       = "Caliber762x39",
            ["7.62x51"]       = "Caliber762x51",
            ["7.62x54R"]      = "Caliber762x54R",
            ["7.62x54 R"]     = "Caliber762x54R",
            ["7.62x25"]       = "Caliber762x25TT",
            ["7.62x25TT"]     = "Caliber762x25TT",
            ["9x18"]          = "Caliber9x18PM",
            ["9x19"]          = "Caliber9x19PARA",
            ["9x21"]          = "Caliber9x21",
            ["9x33R"]         = "Caliber9x33R",
            ["9x39"]          = "Caliber9x39",
            ["4.6x30"]        = "Caliber46x30",
            ["5.7x28"]        = "Caliber57x28",
            ["6.8x51"]        = "Caliber68x51",
            ["12/70"]         = "Caliber12g",
            ["12ga"]          = "Caliber12g",
            ["20/70"]         = "Caliber20g",
            ["20ga"]          = "Caliber20g",
            ["23x75"]         = "Caliber23x75",
            ["26x75"]         = "Caliber26x75",
            ["30x29"]         = "Caliber30x29",
            ["40x46"]         = "Caliber40x46",
            ["40mmRU"]        = "Caliber40mmRU",
            [".366 TKM"]      = "Caliber366TKM",
            [".300 BLK"]      = "Caliber762x35",
            [".300 Blackout"] = "Caliber762x35",
            [".338 LM"]       = "Caliber86x70",
            [".338 Lapua"]    = "Caliber86x70",
            [".45 ACP"]       = "Caliber1143x23ACP",
            [".50 BMG"]       = "Caliber127x99",
            ["12.7x55"]       = "Caliber127x55",
            ["12.7x33"]       = "Caliber127x33",
            [".725"]          = "Caliber725",
        };

    /// <summary>
    /// Converts <paramref name="input"/> to its SPT <c>ammoCaliber</c> ID.
    /// Returns <paramref name="input"/> unchanged when no mapping is found (including
    /// when the input is already in <c>CaliberXXX</c> form).
    /// </summary>
    public static string ToSpt(string input)
    {
        if (DisplayToSpt.TryGetValue(input, out var sptId))
        {
            return sptId;
        }

        return input;
    }
}
