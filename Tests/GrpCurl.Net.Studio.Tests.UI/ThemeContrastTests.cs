using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using GrpCurl.Net.Studio.Tests.UI.Headless;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     V-AUDIT (SPEC-020 §6 / NFR-A5): a static contrast check over the semantic palette. For both the
///     Light and Dark variants it resolves the <em>real</em> themed brushes (so it audits what ships,
///     not a copy of the hex) and asserts every colour used as text meets WCAG 2.1 AA 4.5:1, and every
///     non-text indicator (status dots, the focus outline) meets 3:1, against the surfaces it renders on.
///     <para>
///         Backgrounds are restricted to the surfaces this palette defines — the pane-header and console
///         fills. The document-area base is the Fluent window background, which is lighter (Light) / darker
///         (Dark) than the pane header, so the pane header is the conservative worst-case surface; checking
///         the minimum ratio across both surfaces therefore bounds the real contrast from below.
///     </para>
///     <para>
///         Connection-state dots are checked at the 3:1 non-text bar; they are additionally paired with
///         state words (tooltip + automation name) so state is never conveyed by colour alone (SPEC-020 §6).
///     </para>
/// </summary>
public sealed class ThemeContrastTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    // Foregrounds rendered as text — must clear the 4.5:1 AA bar. (Conn.Connecting is the hyperlink colour.)
    private static readonly string[] TextTokens =
        ["Status.Success", "Status.Neutral", "Status.Caller", "Status.Server", "Conn.Connecting"];

    // Non-text indicators — the 3:1 bar. Status dots + the keyboard focus outline.
    private static readonly string[] NonTextTokens =
        ["Conn.Connected", "Conn.Idle", "Conn.Failed", "Focus.Outline"];

    private static readonly string[] Surfaces = ["Surface.PaneHeader", "Surface.ConsoleBg"];

    public static TheoryData<string> Variants => ["Light", "Dark"];

    [Theory]
    [MemberData(nameof(Variants))]
    public Task Text_colours_meet_AA_4_5_to_1_against_every_surface(string variantName) => RunOnUiThread(() =>
    {
        var variant = Variant(variantName);
        var failures = new List<string>();

        foreach (var token in TextTokens)
        {
            var (surface, ratio) = WorstContrast(token, variant);

            if (ratio < 4.5)
            {
                failures.Add($"{token} on {surface} = {ratio:0.00}:1 (need 4.5:1)");
            }
        }

        failures.ShouldBeEmpty($"{variantName} text contrast below AA: " + string.Join("; ", failures));
    });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task Non_text_indicators_meet_3_to_1_against_every_surface(string variantName) => RunOnUiThread(() =>
    {
        var variant = Variant(variantName);
        var failures = new List<string>();

        foreach (var token in NonTextTokens)
        {
            var (surface, ratio) = WorstContrast(token, variant);

            if (ratio < 3.0)
            {
                failures.Add($"{token} on {surface} = {ratio:0.00}:1 (need 3:1)");
            }
        }

        failures.ShouldBeEmpty($"{variantName} non-text contrast below 3:1: " + string.Join("; ", failures));
    });

    private static (string Surface, double Ratio) WorstContrast(string token, ThemeVariant variant)
    {
        var foreground = Resolve(token, variant);
        var worst = (Surface: string.Empty, Ratio: double.MaxValue);

        foreach (var surface in Surfaces)
        {
            var ratio = ContrastRatio(foreground, Resolve(surface, variant));

            if (ratio < worst.Ratio)
            {
                worst = (surface, ratio);
            }
        }

        return worst;
    }

    private static ThemeVariant Variant(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    // Resolve the per-variant *Color* entry, not the brush. The brushes are defined once at the dictionary
    // root and bind their colour via DynamicResource, so the same brush object serves both variants and would
    // always report the app's live theme — only the "<token>Color" keys live in the per-variant ThemeDictionaries.
    private static Color Resolve(string token, ThemeVariant variant)
    {
        var key = token + "Color";
        Application.Current!.TryGetResource(key, variant, out var value).ShouldBeTrue($"missing resource {key}");
        return (Color)value!;
    }

    // WCAG 2.1 relative luminance + contrast ratio.
    private static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte channel)
    {
        var s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
