namespace MargPos.Shared;

/// <summary>
/// Code 128 encoder for shelf and lot labels: every handheld scanner reads it, and it carries letters as well as
/// digits. All-digit text of even length uses code set C (two digits per symbol, half the width); anything else
/// uses code set B (printable ASCII).
/// </summary>
public static class Code128
{
    private const int StartB = 104, StartC = 105, Stop = 106;

    /// <summary>Bar/space widths in modules for symbol values 0–106, bar first. 106 is the stop pattern.</summary>
    public static readonly IReadOnlyList<string> Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    public static bool CanEncode(string text) => text.Length > 0 && text.All(c => c is >= ' ' and <= '~');

    /// <summary>Symbol values including start, check symbol and stop.</summary>
    public static IReadOnlyList<int> Symbols(string text)
    {
        if (!CanEncode(text)) throw new ArgumentException("Code 128 labels take printable ASCII characters only.", nameof(text));

        var useC = text.Length % 2 == 0 && text.All(char.IsAsciiDigit);
        var symbols = new List<int> { useC ? StartC : StartB };
        if (useC)
            for (var i = 0; i < text.Length; i += 2) symbols.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
        else
            symbols.AddRange(text.Select(c => c - ' '));

        var check = symbols[0];
        for (var i = 1; i < symbols.Count; i++) check += symbols[i] * i;
        symbols.Add(check % 103);
        symbols.Add(Stop);
        return symbols;
    }

    /// <summary>Module widths alternating bar, space, bar… from the first bar to the last (no quiet zones).</summary>
    public static IReadOnlyList<int> Modules(string text) =>
        Symbols(text).SelectMany(s => Patterns[s].Select(c => c - '0')).ToList();
}
