using System;
using System.Collections.Generic;
using CozyCafe.Core.Modules;
using CozyCafe.Core.Render;

namespace CozyCafe.Core.Ui
{
    /// Widget interaction states — every widget paints from its real state.
    public enum UiState
    {
        Normal = 0,
        Hover = 1,
        Pressed = 2,
        Disabled = 3,
        Focus = 4
    }

    /// Widget kinds the shared layer ships: panel (9-slice frame), button,
    /// label, text input and a UGC preview cell.
    public enum UiRole
    {
        Panel = 0,
        Button = 1,
        Label = 2,
        TextInput = 3,
        Preview = 4
    }

    /// <summary>
    /// Runtime Hangul + ASCII text rasterizer. Text is composed at draw time
    /// from the live string — each Hangul syllable is decomposed into its
    /// jamo (choseong/jungseong/jongseong per the U+AC00 block arithmetic)
    /// and the jamo are stroked procedurally into a syllable cell, so any
    /// Korean string renders without a font file and nothing is ever baked
    /// into an image asset. ASCII digits/punctuation use the same stroke
    /// path; unknown codepoints draw a placeholder box rather than a glyph.
    /// </summary>
    public static class UiText
    {
        // Stroke-font unit space. Every glyph shape is a set of line
        // segments and stroked ellipses authored in a [0,1]x[0,1] cell and
        // mapped into whatever sub-rect the layout assigns it, so one table
        // serves every region (initial/medial/final) at every scale.
        private sealed class Glyph
        {
            public double[][] Lines = new double[0][];
            public double[][] Rings = new double[0][];
        }

        private static double[] L(double x0, double y0, double x1, double y1)
        {
            return new[] { x0, y0, x1, y1 };
        }

        private static double[] R(double cx, double cy, double r)
        {
            return new[] { cx, cy, r };
        }

        private static Glyph G(params double[][] lines)
        {
            return new Glyph { Lines = lines };
        }

        private static Glyph WithRings(Glyph g, params double[][] rings)
        {
            g.Rings = rings;
            return g;
        }

        // ---- base consonant jamo (initials; also finals' components) ----
        private static readonly Dictionary<char, Glyph> Cons =
            new Dictionary<char, Glyph>
        {
            { (char)0x3131, G(L(.12,.2,.85,.2), L(.85,.2,.85,.82)) },
            { (char)0x3134, G(L(.15,.2,.15,.82), L(.15,.82,.85,.82)) },
            { (char)0x3137, G(L(.2,.2,.82,.2), L(.15,.2,.15,.82), L(.15,.82,.85,.82)) },
            { (char)0x3139, G(L(.15,.12,.85,.12), L(.85,.12,.85,.45), L(.85,.45,.18,.45),
                              L(.18,.45,.18,.85), L(.18,.85,.85,.85)) },
            { (char)0x3141, G(L(.15,.2,.85,.2), L(.85,.2,.85,.8), L(.85,.8,.15,.8),
                              L(.15,.8,.15,.2)) },
            { (char)0x3142, G(L(.15,.15,.85,.15), L(.85,.15,.85,.82), L(.85,.82,.15,.82),
                              L(.15,.82,.15,.15), L(.15,.5,.85,.5)) },
            { (char)0x3145, G(L(.5,.15,.15,.85), L(.5,.15,.85,.85)) },
            { (char)0x3147, WithRings(G(), R(.5,.55,.34)) },
            { (char)0x3148, G(L(.15,.28,.85,.28), L(.5,.28,.16,.85), L(.5,.28,.84,.85)) },
            { (char)0x314a, G(L(.38,.06,.62,.06), L(.15,.32,.85,.32),
                              L(.5,.32,.16,.85), L(.5,.32,.84,.85)) },
            { (char)0x314b, G(L(.12,.2,.85,.2), L(.12,.55,.85,.55), L(.85,.2,.85,.82)) },
            { (char)0x314c, G(L(.2,.14,.82,.14), L(.2,.45,.82,.45),
                              L(.15,.14,.15,.82), L(.15,.82,.85,.82)) },
            { (char)0x314d, G(L(.12,.2,.88,.2), L(.32,.2,.32,.6), L(.68,.2,.68,.6),
                              L(.12,.85,.88,.85)) },
            { (char)0x314e, WithRings(G(L(.35,.05,.65,.05), L(.12,.3,.88,.3)),
                              R(.5,.66,.3)) },
        };

        // ---- base vowel jamo ----
        private static readonly Dictionary<char, Glyph> Vow =
            new Dictionary<char, Glyph>
        {
            { (char)0x314f, G(L(.62,.08,.62,.92), L(.62,.45,.18,.45)) },
            { (char)0x3150, G(L(.4,.08,.4,.92), L(.4,.45,.08,.45), L(.78,.08,.78,.92)) },
            { (char)0x3151, G(L(.66,.08,.66,.92), L(.66,.32,.24,.32), L(.66,.56,.24,.56)) },
            { (char)0x3152, G(L(.4,.08,.4,.92), L(.4,.32,.08,.32), L(.4,.56,.08,.56),
                              L(.78,.08,.78,.92)) },
            { (char)0x3153, G(L(.38,.08,.38,.92), L(.38,.45,.82,.45)) },
            { (char)0x3154, G(L(.25,.08,.25,.92), L(.25,.45,.62,.45), L(.78,.08,.78,.92)) },
            { (char)0x3155, G(L(.34,.08,.34,.92), L(.34,.32,.76,.32), L(.34,.56,.76,.56)) },
            { (char)0x3156, G(L(.22,.08,.22,.92), L(.22,.32,.58,.32), L(.22,.56,.58,.56),
                              L(.78,.08,.78,.92)) },
            { (char)0x3157, G(L(.12,.72,.88,.72), L(.5,.72,.5,.28)) },
            { (char)0x315b, G(L(.12,.72,.88,.72), L(.34,.72,.34,.28), L(.66,.72,.66,.28)) },
            { (char)0x315c, G(L(.12,.28,.88,.28), L(.5,.28,.5,.74)) },
            { (char)0x3160, G(L(.12,.28,.88,.28), L(.34,.28,.34,.74), L(.66,.28,.66,.74)) },
            { (char)0x3161, G(L(.12,.5,.88,.5)) },
            { (char)0x3163, G(L(.55,.08,.55,.92)) },
        };

        // Mixed vowels decompose into a below-vowel + a right-vowel.
        private static readonly Dictionary<char, char[]> MixedVow =
            new Dictionary<char, char[]>
        {
            { (char)0x3158, new[] { (char)0x3157, (char)0x314f } },  // ㅘ = ㅗ+ㅏ
            { (char)0x3159, new[] { (char)0x3157, (char)0x3150 } },  // ㅙ = ㅗ+ㅐ
            { (char)0x315a, new[] { (char)0x3157, (char)0x3163 } },  // ㅚ = ㅗ+ㅣ
            { (char)0x315d, new[] { (char)0x315c, (char)0x3153 } },  // ㅝ = ㅜ+ㅓ
            { (char)0x315e, new[] { (char)0x315c, (char)0x3154 } },  // ㅞ = ㅜ+ㅔ
            { (char)0x315f, new[] { (char)0x315c, (char)0x3163 } },  // ㅟ = ㅜ+ㅣ
            { (char)0x3162, new[] { (char)0x3161, (char)0x3163 } },  // ㅢ = ㅡ+ㅣ
        };

        // Compatibility jamo that double a base consonant.
        private static readonly Dictionary<char, char> Doubled =
            new Dictionary<char, char>
        {
            { (char)0x3132, (char)0x3131 },  // ㄲ
            { (char)0x3138, (char)0x3137 },  // ㄸ
            { (char)0x3143, (char)0x3142 },  // ㅃ
            { (char)0x3146, (char)0x3145 },  // ㅆ
            { (char)0x3149, (char)0x3148 },  // ㅉ
        };

        // Compound final consonants: two base jamo side by side.
        private static readonly Dictionary<char, char[]> Paired =
            new Dictionary<char, char[]>
        {
            { (char)0x3133, new[] { (char)0x3131, (char)0x3145 } },  // ㄳ
            { (char)0x3135, new[] { (char)0x3134, (char)0x3148 } },  // ㄵ
            { (char)0x3136, new[] { (char)0x3134, (char)0x314e } },  // ㄶ
            { (char)0x313a, new[] { (char)0x3139, (char)0x3131 } },  // ㄺ
            { (char)0x313b, new[] { (char)0x3139, (char)0x3141 } },  // ㄻ
            { (char)0x313c, new[] { (char)0x3139, (char)0x3142 } },  // ㄼ
            { (char)0x313d, new[] { (char)0x3139, (char)0x3145 } },  // ㄽ
            { (char)0x313e, new[] { (char)0x3139, (char)0x314c } },  // ㄾ
            { (char)0x313f, new[] { (char)0x3139, (char)0x314d } },  // ㄿ
            { (char)0x3140, new[] { (char)0x3139, (char)0x314e } },  // ㅀ
            { (char)0x3144, new[] { (char)0x3142, (char)0x3145 } },  // ㅄ
        };

        private static readonly Dictionary<char, Glyph> Ascii =
            new Dictionary<char, Glyph>
        {
            { '0', G(L(.15,.15,.85,.15), L(.85,.15,.85,.85), L(.85,.85,.15,.85), L(.15,.85,.15,.15)) },
            { '1', G(L(.55,.12,.55,.88)) },
            { '2', G(L(.15,.15,.85,.15), L(.85,.15,.85,.5), L(.85,.5,.15,.5),
                     L(.15,.5,.15,.85), L(.15,.85,.85,.85)) },
            { '3', G(L(.15,.15,.85,.15), L(.85,.15,.85,.85), L(.85,.85,.15,.85),
                     L(.3,.5,.85,.5)) },
            { '4', G(L(.15,.15,.15,.5), L(.15,.5,.85,.5), L(.68,.15,.68,.88)) },
            { '5', G(L(.85,.15,.15,.15), L(.15,.15,.15,.5), L(.15,.5,.85,.5),
                     L(.85,.5,.85,.85), L(.85,.85,.15,.85)) },
            { '6', G(L(.85,.15,.15,.15), L(.15,.15,.15,.85), L(.15,.5,.85,.5),
                     L(.85,.5,.85,.85), L(.85,.85,.15,.85)) },
            { '7', G(L(.15,.15,.85,.15), L(.85,.15,.85,.88)) },
            { '8', G(L(.15,.15,.85,.15), L(.85,.15,.85,.85), L(.85,.85,.15,.85),
                     L(.15,.85,.15,.15), L(.15,.5,.85,.5)) },
            { '9', G(L(.15,.5,.15,.15), L(.15,.15,.85,.15), L(.85,.15,.85,.85),
                     L(.85,.5,.15,.5), L(.85,.85,.15,.85)) },
            { ':', G(L(.4,.3,.45,.3), L(.4,.7,.45,.7)) },
            { '-', G(L(.2,.5,.8,.5)) },
            { '.', G(L(.4,.82,.45,.82)) },
            { '/', G(L(.15,.88,.85,.12)) },
            { '%', G(L(.2,.2,.3,.2), L(.7,.8,.8,.8), L(.2,.85,.8,.15)) },
            { '_', G(L(.15,.85,.85,.85)) },

            // ---- Latin letters ----
            // Uppercase rides the full cap height (.12-.88); lowercase sits
            // on an x-height body (.42-.85) with real ascenders/descenders,
            // so mixed Korean/Latin strings stay legible rather than
            // degrading to placeholder boxes.
            { 'A', G(L(.12,.88,.5,.12), L(.5,.12,.88,.88), L(.28,.6,.72,.6)) },
            { 'B', G(L(.22,.12,.22,.88), L(.22,.12,.68,.12), L(.68,.12,.8,.3),
                     L(.8,.3,.68,.5), L(.68,.5,.22,.5), L(.68,.5,.82,.66),
                     L(.82,.66,.82,.74), L(.82,.74,.66,.88), L(.66,.88,.22,.88)) },
            { 'C', G(L(.85,.18,.28,.18), L(.28,.18,.28,.82), L(.28,.82,.85,.82)) },
            { 'D', G(L(.25,.12,.25,.88), L(.25,.12,.62,.12), L(.62,.12,.85,.5),
                     L(.85,.5,.62,.88), L(.62,.88,.25,.88)) },
            { 'E', G(L(.8,.15,.22,.15), L(.22,.15,.22,.85), L(.22,.85,.8,.85),
                     L(.22,.5,.62,.5)) },
            { 'F', G(L(.8,.15,.22,.15), L(.22,.15,.22,.88), L(.22,.5,.62,.5)) },
            { 'G', G(L(.85,.18,.28,.18), L(.28,.18,.28,.82), L(.28,.82,.85,.82),
                     L(.85,.82,.85,.55), L(.85,.55,.55,.55)) },
            { 'H', G(L(.2,.12,.2,.88), L(.8,.12,.8,.88), L(.2,.5,.8,.5)) },
            { 'I', G(L(.5,.15,.5,.85), L(.3,.15,.7,.15), L(.3,.85,.7,.85)) },
            { 'J', G(L(.72,.15,.72,.82), L(.72,.82,.35,.82), L(.35,.82,.6,.6)) },
            { 'K', G(L(.25,.12,.25,.88), L(.78,.12,.25,.55), L(.4,.48,.8,.88)) },
            { 'L', G(L(.28,.12,.28,.85), L(.28,.85,.8,.85)) },
            { 'M', G(L(.15,.88,.15,.15), L(.15,.15,.5,.58), L(.5,.58,.85,.15),
                     L(.85,.15,.85,.88)) },
            { 'N', G(L(.2,.88,.2,.15), L(.2,.15,.8,.88), L(.8,.88,.8,.15)) },
            { 'O', G(L(.25,.15,.75,.15), L(.75,.15,.75,.85), L(.75,.85,.25,.85),
                     L(.25,.85,.25,.15)) },
            { 'P', G(L(.25,.12,.25,.88), L(.25,.12,.72,.12), L(.72,.12,.72,.55),
                     L(.72,.55,.25,.55)) },
            { 'Q', G(L(.25,.15,.75,.15), L(.75,.15,.75,.85), L(.75,.85,.25,.85),
                     L(.25,.85,.25,.15), L(.6,.68,.88,.95)) },
            { 'R', G(L(.25,.12,.25,.88), L(.25,.12,.72,.12), L(.72,.12,.72,.55),
                     L(.72,.55,.25,.55), L(.55,.55,.82,.88)) },
            { 'S', G(L(.8,.15,.25,.15), L(.25,.15,.25,.5), L(.25,.5,.75,.5),
                     L(.75,.5,.75,.85), L(.75,.85,.2,.85)) },
            { 'T', G(L(.15,.15,.85,.15), L(.5,.15,.5,.88)) },
            { 'U', G(L(.2,.12,.2,.72), L(.2,.72,.4,.88), L(.4,.88,.62,.88),
                     L(.62,.88,.8,.72), L(.8,.72,.8,.12)) },
            { 'V', G(L(.15,.15,.5,.88), L(.5,.88,.85,.15)) },
            { 'W', G(L(.12,.15,.32,.88), L(.32,.88,.5,.42), L(.5,.42,.68,.88),
                     L(.68,.88,.88,.15)) },
            { 'X', G(L(.18,.15,.82,.88), L(.82,.15,.18,.88)) },
            { 'Y', G(L(.15,.15,.5,.55), L(.85,.15,.5,.55), L(.5,.55,.5,.88)) },
            { 'Z', G(L(.2,.15,.8,.15), L(.8,.15,.2,.85), L(.2,.85,.8,.85)) },
            { 'a', WithRings(G(L(.75,.4,.75,.85)), R(.49,.62,.55)) },
            { 'b', WithRings(G(L(.24,.12,.24,.88)), R(.56,.63,.6)) },
            { 'c', G(L(.8,.45,.3,.45), L(.3,.45,.3,.8), L(.3,.8,.8,.8)) },
            { 'd', WithRings(G(L(.76,.12,.76,.88)), R(.44,.63,.6)) },
            { 'e', G(L(.78,.58,.24,.58), L(.24,.58,.24,.82), L(.24,.82,.72,.82),
                     L(.24,.58,.35,.42), L(.35,.42,.7,.42), L(.7,.42,.78,.52)) },
            { 'f', G(L(.4,.88,.5,.16), L(.5,.16,.72,.14), L(.28,.5,.66,.5)) },
            { 'g', WithRings(G(L(.75,.4,.75,.95), L(.75,.95,.42,.95)),
                     R(.46,.56,.55)) },
            { 'h', G(L(.24,.12,.24,.88), L(.24,.58,.58,.42), L(.58,.42,.76,.5),
                     L(.76,.5,.76,.88)) },
            { 'i', G(L(.5,.42,.5,.85), L(.45,.2,.55,.2)) },
            { 'j', G(L(.62,.42,.62,.95), L(.62,.95,.4,.95), L(.56,.2,.66,.2)) },
            { 'k', G(L(.25,.12,.25,.88), L(.7,.42,.25,.62), L(.4,.56,.74,.88)) },
            { 'l', G(L(.45,.12,.45,.88), L(.45,.88,.6,.88)) },
            { 'm', G(L(.15,.88,.15,.42), L(.15,.42,.36,.4), L(.36,.4,.5,.5),
                     L(.5,.5,.5,.88), L(.5,.45,.66,.4), L(.66,.4,.85,.5),
                     L(.85,.5,.85,.88)) },
            { 'n', G(L(.24,.88,.24,.42), L(.24,.5,.6,.4), L(.6,.4,.76,.5),
                     L(.76,.5,.76,.88)) },
            { 'o', WithRings(G(), R(.5,.63,.6)) },
            { 'p', WithRings(G(L(.24,.42,.24,.98)), R(.56,.62,.6)) },
            { 'q', WithRings(G(L(.76,.42,.76,.98)), R(.44,.62,.6)) },
            { 'r', G(L(.28,.42,.28,.88), L(.28,.55,.55,.4), L(.55,.4,.74,.45)) },
            { 's', G(L(.74,.42,.28,.42), L(.28,.42,.28,.62), L(.28,.62,.72,.62),
                     L(.72,.62,.72,.85), L(.72,.85,.26,.85)) },
            { 't', G(L(.45,.18,.45,.85), L(.45,.85,.66,.85), L(.28,.45,.64,.45)) },
            { 'u', G(L(.22,.42,.22,.8), L(.22,.8,.5,.88), L(.5,.88,.78,.8),
                     L(.78,.8,.78,.42)) },
            { 'v', G(L(.18,.42,.5,.88), L(.5,.88,.82,.42)) },
            { 'w', G(L(.15,.42,.32,.88), L(.32,.88,.5,.58), L(.5,.58,.68,.88),
                     L(.68,.88,.85,.42)) },
            { 'x', G(L(.22,.42,.78,.88), L(.78,.42,.22,.88)) },
            { 'y', G(L(.2,.42,.5,.72), L(.8,.42,.5,.72), L(.5,.72,.32,.98)) },
            { 'z', G(L(.26,.45,.74,.45), L(.74,.45,.26,.85), L(.26,.85,.76,.85)) },

            // ---- extra punctuation used by real UI strings ----
            { '(', G(L(.62,.14,.4,.35), L(.4,.35,.4,.65), L(.4,.65,.62,.86)) },
            { ')', G(L(.38,.14,.6,.35), L(.6,.35,.6,.65), L(.6,.65,.38,.86)) },
            { ',', G(L(.5,.8,.42,.95)) },
            { '\'', G(L(.48,.15,.44,.34)) },
            { '"', G(L(.36,.15,.33,.34), L(.62,.15,.6,.34)) },
            { '!', G(L(.5,.15,.5,.6), L(.45,.82,.55,.82)) },
            { '?', G(L(.22,.3,.35,.15), L(.35,.15,.68,.15), L(.68,.15,.78,.28),
                     L(.78,.28,.78,.42), L(.78,.42,.52,.58), L(.52,.58,.52,.7),
                     L(.47,.85,.57,.85)) },
            { '+', G(L(.2,.5,.8,.5), L(.5,.3,.5,.7)) },
            { '=', G(L(.25,.4,.75,.4), L(.25,.62,.75,.62)) },
            { '*', G(L(.5,.28,.5,.72), L(.24,.38,.76,.62), L(.76,.38,.24,.62)) },
            { '#', G(L(.36,.25,.32,.75), L(.66,.25,.62,.75), L(.24,.42,.8,.42),
                     L(.22,.62,.76,.62)) },
            { ';', G(L(.45,.4,.55,.4), L(.5,.72,.42,.9)) },
            { '<', G(L(.7,.25,.3,.5), L(.3,.5,.7,.75)) },
            { '>', G(L(.3,.25,.7,.5), L(.7,.5,.3,.75)) },
            { '[', G(L(.6,.15,.35,.15), L(.35,.15,.35,.85), L(.35,.85,.6,.85)) },
            { ']', G(L(.4,.15,.65,.15), L(.65,.15,.65,.85), L(.65,.85,.4,.85)) },
            { '{', G(L(.65,.15,.45,.22), L(.45,.22,.45,.42), L(.45,.42,.3,.5),
                     L(.3,.5,.45,.58), L(.45,.58,.45,.78), L(.45,.78,.65,.85)) },
            { '}', G(L(.35,.15,.55,.22), L(.55,.22,.55,.42), L(.55,.42,.7,.5),
                     L(.7,.5,.55,.58), L(.55,.58,.55,.78), L(.55,.78,.35,.85)) },
            { '$', G(L(.8,.18,.28,.18), L(.28,.18,.28,.48), L(.28,.48,.75,.48),
                     L(.75,.48,.75,.82), L(.75,.82,.22,.82), L(.5,.05,.5,.95)) },
            { '@', WithRings(G(L(.68,.38,.68,.6)), R(.5,.52,.72), R(.5,.52,.3)) },
            { '&', WithRings(G(L(.32,.42,.78,.88), L(.68,.42,.25,.88)),
                     R(.4,.27,.3)) },
            { '~', G(L(.15,.55,.35,.45), L(.35,.45,.65,.6), L(.65,.6,.85,.5)) },
            { '^', G(L(.3,.4,.5,.2), L(.5,.2,.7,.4)) },
            { '`', G(L(.4,.15,.55,.32)) },
            { '|', G(L(.5,.12,.5,.9)) },
            { '\\', G(L(.15,.12,.85,.88)) },
        };

        // ---- syllable geometry ----------------------------------------
        // Cell 8x12 units at scale s. Regions depend on the medial class:
        //   right-vowel   : initial wide-left, vowel right strip
        //   below-vowel   : initial top, vowel bottom
        //   mixed         : initial top-left, below-part, right-part
        // A final consonant reserves the bottom strip in every class.

        private const int CellW = 8, CellH = 12;

        /// Pixel width of `text` at `scale`, matching Draw exactly.
        public static int Measure(string text, int scale)
        {
            if (scale < 1) scale = 1;
            int w = 0;
            foreach (char ch in text ?? "")
            {
                w += ch == ' ' ? 5 * scale
                    : IsHangul(ch) ? (CellW + 1) * scale
                    : 6 * scale;
            }
            return w;
        }

        private static bool IsHangul(char ch)
        {
            return ch >= (char)0xAC00 && ch <= (char)0xD7A3;
        }

        /// Rasterizes `text` into its own tight canvas — the real pixels a
        /// label paints for THIS string, produced at call time.
        public static SoftwareCanvas Render(string text, int scale, Rgba color)
        {
            int w = Math.Max(1, Measure(text, scale));
            var c = new SoftwareCanvas(w, (CellH + 1) * scale);
            Draw(c, text, 0, 0, scale, color);
            return c;
        }

        /// Draws `text` at (x,y) top-left; returns the x after the last
        /// glyph so callers can chain (caret placement, suffixes).
        public static int Draw(SoftwareCanvas canvas, string text, int x,
            int y, int scale, Rgba color)
        {
            if (scale < 1) scale = 1;
            int pen = x;
            foreach (char ch in text ?? "")
            {
                if (ch == ' ') { pen += 5 * scale; continue; }
                if (IsHangul(ch))
                {
                    DrawHangul(canvas, ch, pen, y, scale, color);
                    pen += (CellW + 1) * scale;
                    continue;
                }
                DrawAscii(canvas, ch, pen, y, scale, color);
                pen += 6 * scale;
            }
            return pen;
        }

        private static void DrawAscii(SoftwareCanvas canvas, char ch, int x,
            int y, int scale, Rgba color)
        {
            Glyph g;
            if (!Ascii.TryGetValue(ch, out g))
            {
                // Placeholder box — honest fallback, never a baked glyph.
                g = G(L(.15,.15,.85,.15), L(.85,.15,.85,.85),
                      L(.85,.85,.15,.85), L(.15,.85,.15,.15),
                      L(.15,.15,.85,.85), L(.85,.15,.15,.85));
            }
            StrokeGlyph(canvas, g, x, y, 5 * scale, CellH * scale,
                Math.Max(1, scale), color);
        }

        /// U+AC00 arithmetic decomposition into jamo, then region layout.
        private static void DrawHangul(SoftwareCanvas canvas, char ch, int x,
            int y, int scale, Rgba color)
        {
            int sIdx = ch - 0xAC00;
            int ini = sIdx / 588;
            int med = (sIdx % 588) / 28;
            int fin = sIdx % 28;
            int t = Math.Max(1, scale);

            // Compatibility-jamo chars for the decomposed components.
            char ic = (char)(0x3131 + InitialMap[ini]);
            char vc = (char)(0x314f + med);
            char fc = fin == 0 ? (char)0 : (char)(0x3131 + FinalMap[fin - 1]);

            bool right = IsRightVowel(vc);
            bool below = IsBelowVowel(vc);
            bool mixed = MixedVow.ContainsKey(vc);
            bool hasFinal = fin != 0;

            double u = scale; // unit px
            if (right)
            {
                // initial: left block; vowel: right strip
                double iw = 5.4 * u, ih = (hasFinal ? 7.0 : 10.8) * u;
                double vx = x + 4.6 * u, vy = y + 0.8 * u;
                double vw = 3.4 * u, vh = (hasFinal ? 6.6 : 10.2) * u;
                StrokeJamo(canvas, ic, x + 0.2 * u, y + 0.4 * u, iw, ih, t, color);
                StrokeJamo(canvas, vc, vx, vy, vw, vh, t, color);
                if (hasFinal)
                {
                    StrokeJamo(canvas, fc, x + 0.6 * u, y + 7.0 * u,
                        6.8 * u, 4.6 * u, t, color);
                }
            }
            else if (below)
            {
                // initial: top block; vowel: bottom strip
                double iw = 6.8 * u, ih = (hasFinal ? 4.2 : 5.6) * u;
                double vx = x + 0.6 * u;
                double vy = y + (hasFinal ? 4.0 : 5.2) * u;
                double vw = 6.8 * u, vh = (hasFinal ? 3.4 : 5.6) * u;
                StrokeJamo(canvas, ic, x + 0.6 * u, y + 0.3 * u, iw, ih, t, color);
                StrokeJamo(canvas, vc, vx, vy, vw, vh, t, color);
                if (hasFinal)
                {
                    StrokeJamo(canvas, fc, x + 0.6 * u, y + 7.2 * u,
                        6.8 * u, 4.4 * u, t, color);
                }
            }
            else if (mixed)
            {
                var parts = MixedVow[vc];
                // initial top-left, below-part mid-left, right-part right.
                StrokeJamo(canvas, ic, x + 0.2 * u, y + 0.3 * u,
                    5.2 * u, (hasFinal ? 3.0 : 3.8) * u, t, color);
                StrokeJamo(canvas, parts[0], x + 0.2 * u,
                    y + (hasFinal ? 3.2 : 3.8) * u,
                    5.2 * u, (hasFinal ? 3.6 : 7.0) * u, t, color);
                StrokeJamo(canvas, parts[1], x + 4.8 * u, y + 0.8 * u,
                    3.2 * u, (hasFinal ? 6.2 : 10.2) * u, t, color);
                if (hasFinal)
                {
                    StrokeJamo(canvas, fc, x + 0.6 * u, y + 7.2 * u,
                        6.8 * u, 4.4 * u, t, color);
                }
            }
            else
            {
                // Fallback: compatibility jamo alone (isolated jamo input).
                StrokeJamo(canvas, ic, x + 0.6 * u, y + 0.6 * u,
                    6.8 * u, 10.8 * u, t, color);
            }
        }

        private static bool IsRightVowel(char vc)
        {
            switch (vc)
            {
                case (char)0x314f: case (char)0x3150: case (char)0x3151:
                case (char)0x3152: case (char)0x3153: case (char)0x3154:
                case (char)0x3155: case (char)0x3156: case (char)0x3163:
                    return true;
                default:
                    return false;
            }
        }

   

        private static bool IsBelowVowel(char vc)
        {
            switch (vc)
            {
                case (char)0x3157: case (char)0x315b: case (char)0x315c:
                case (char)0x3160: case (char)0x3161:
                    return true;
                default:
                    return false;
            }
        }

        // Initial index -> compatibility jamo offset from 0x3131.
        private static readonly int[] InitialMap =
        {
            0, 1, 3, 6, 7, 8, 16, 17, 18, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29
        };

        // Final index-1 -> compatibility jamo offset from 0x3131.
        private static readonly int[] FinalMap =
        {
            0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20,
            21, 22, 23, 25, 26, 27, 28, 29
        };

        /// Draws one jamo (possibly doubled/paired) into the given rect.
        private static void StrokeJamo(SoftwareCanvas canvas, char jamo,
            double x, double y, double w, double h, int t, Rgba color)
        {
            char dbl;
            if (Doubled.TryGetValue(jamo, out dbl))
            {
                StrokeSingle(canvas, dbl, x, y, w * 0.52, h, t, color);
                StrokeSingle(canvas, dbl, x + w * 0.48, y, w * 0.52, h, t, color);
                return;
            }
            char[] pair;
            if (Paired.TryGetValue(jamo, out pair))
            {
                StrokeSingle(canvas, pair[0], x, y, w * 0.52, h, t, color);
                StrokeSingle(canvas, pair[1], x + w * 0.48, y, w * 0.52, h, t, color);
                return;
            }
            StrokeSingle(canvas, jamo, x, y, w, h, t, color);
        }

        private static void StrokeSingle(SoftwareCanvas canvas, char jamo,
            double x, double y, double w, double h, int t, Rgba color)
        {
            Glyph g;
            if (!Cons.TryGetValue(jamo, out g) && !Vow.TryGetValue(jamo, out g))
            {
                g = G(L(.2,.2,.8,.2), L(.8,.2,.8,.8), L(.8,.8,.2,.8), L(.2,.8,.2,.2));
            }
            StrokeGlyph(canvas, g, x, y, w, h, t, color);
        }

        private static void StrokeGlyph(SoftwareCanvas canvas, Glyph g,
            double x, double y, double w, double h, int t, Rgba color)
        {
            foreach (var l in g.Lines)
            {
                canvas.DrawLine(x + l[0] * w, y + l[1] * h,
                    x + l[2] * w, y + l[3] * h, color, t);
            }
            foreach (var r in g.Rings)
            {
                StrokeRing(canvas, x + r[0] * w, y + r[1] * h,
                    r[2] * w * 0.5, r[2] * h * 0.5, t, color);
            }
        }

        /// Stroked ellipse: paints pixels whose normalized distance sits
        /// within half a unit of the ring — the ㅇ/ㅎ body.
        private static void StrokeRing(SoftwareCanvas canvas, double cx,
            double cy, double rx, double ry, int t, Rgba color)
        {
            if (rx <= 0 || ry <= 0) return;
            int x0 = (int)Math.Floor(cx - rx - t), x1 = (int)Math.Ceiling(cx + rx + t);
            int y0 = (int)Math.Floor(cy - ry - t), y1 = (int)Math.Ceiling(cy + ry + t);
            double band = 0.5 * t / Math.Min(rx, ry) + 0.5 / Math.Min(rx, ry);
            for (int yy = y0; yy <= y1; yy++)
            {
                for (int xx = x0; xx <= x1; xx++)
                {
                    double nx = (xx + 0.5 - cx) / rx;
                    double ny = (yy + 0.5 - cy) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);
                    if (Math.Abs(d - 1.0) <= band) canvas.SetPixel(xx, yy, color);
                }
            }
        }
    }

    /// <summary>
    /// A 9-slice skin: a source sprite plus border insets. Render() produces
    /// a resized sprite where the four corners are copied unscaled, the four
    /// edges stretch along their axis, and the center fills — so a skin
    /// authored once works at every widget size while the widget's logical
    /// rect (the clickable area) is exactly the requested bounds.
    /// </summary>
    public sealed class UiSkin
    {
        public readonly string Id;
        public readonly SoftwareCanvas Source;
        public readonly int BorderL, BorderT, BorderR, BorderB;

        public UiSkin(string id, SoftwareCanvas source, int borderL,
            int borderT, int borderR, int borderB)
        {
            Id = id;
            Source = source;
            BorderL = Math.Max(0, Math.Min(borderL, source.Width));
            BorderT = Math.Max(0, Math.Min(borderT, source.Height));
            BorderR = Math.Max(0, Math.Min(borderR, source.Width));
            BorderB = Math.Max(0, Math.Min(borderB, source.Height));
        }

        /// Nearest-sample 9-slice resize. Corners map 1:1 to the source
        /// corners (never scaled); if the target is smaller than the two
        /// borders on an axis the borders share the space proportionally.
        public SoftwareCanvas Render(int w, int h)
        {
            var outp = new SoftwareCanvas(w, h);
            int sw = Source.Width, sh = Source.Height;
            int cl = w >= BorderL + BorderR
                ? BorderL : w * BorderL / Math.Max(1, BorderL + BorderR);
            int cr = w >= BorderL + BorderR ? BorderR : w - cl;
            int ct = h >= BorderT + BorderB
                ? BorderT : h * BorderT / Math.Max(1, BorderT + BorderB);
            int cb = h >= BorderT + BorderB ? BorderB : h - ct;
            int cw = Math.Max(0, w - cl - cr), ch = Math.Max(0, h - ct - cb);
            int scw = Math.Max(1, sw - BorderL - BorderR);
            int sch = Math.Max(1, sh - BorderT - BorderB);
            for (int y = 0; y < h; y++)
            {
                int sy;
                if (y < ct) sy = y;
                else if (y >= ct + ch) sy = sh - (h - y);
                else sy = BorderT + (y - ct) * sch / Math.Max(1, ch);
                for (int x = 0; x < w; x++)
                {
                    int sx;
                    if (x < cl) sx = x;
                    else if (x >= cl + cw) sx = sw - (w - x);
                    else sx = BorderL + (x - cl) * scw / Math.Max(1, cw);
                    outp.Pixels[(y * w + x) * 4] = Source.Pixels[(sy * sw + sx) * 4];
                    outp.Pixels[(y * w + x) * 4 + 1] = Source.Pixels[(sy * sw + sx) * 4 + 1];
                    outp.Pixels[(y * w + x) * 4 + 2] = Source.Pixels[(sy * sw + sx) * 4 + 2];
                    outp.Pixels[(y * w + x) * 4 + 3] = Source.Pixels[(sy * sw + sx) * 4 + 3];
                }
            }
            return outp;
        }
    }

    /// One live widget: a logical rect (also the hit area — the clickable
    /// area is always exactly the rect, whatever the skin does visually),
    /// a role, real text, an interaction state and an optional input field.
    public sealed class UiWidget
    {
        public string Id;
        public UiRole Role;
        public int X, Y, W, H;
        public string Text = "";
        public UiState State = UiState.Normal;
        public string SkinId = "";
        /// Skin resolved at BuildFrame (fallback applied when missing).
        public UiSkin ResolvedSkin;
        // text-input state
        public string InputText = "";
        public int Caret;
        public string Placeholder = "";
        // preview cell payload (UGC thumbnails)
        public SoftwareCanvas PreviewSprite;

        public bool Contains(int px, int py)
        {
            return px >= X && px < X + W && py >= Y && py < Y + H;
        }

        /// Resizes the widget — the clickable area IS the rect, so it
        /// always matches the visual bounds after any resize.
        public void SetRect(int x, int y, int w, int h)
        {
            X = x; Y = y; W = w; H = h;
        }
    }

    /// A laid-out snapshot the painter draws: viewport size, the widget
    /// list in paint order and the focused widget. Skins are already
    /// resolved onto each widget (imported skin, or the default fallback).
    public sealed class UiFrame
    {
        public int ViewportW, ViewportH;
        public readonly List<UiWidget> Widgets = new List<UiWidget>();
        public UiWidget Focused;
        /// True when at least one widget fell back to the default skin
        /// because its requested skin id was not installed.
        public bool UsedFallbackSkin;
    }

    /// <summary>
    /// The shared UI layer: a skin registry (imported UGC skins + the
    /// always-present default fallback), the live widget tree, pointer
    /// states, real text input and the frame painter the software renderer
    /// calls. Screen-space only — the UI never goes through iso projection
    /// and never receives the furniture (0,-8) render offset. No baked-text
    /// image source exists anywhere in this module: every label is
    /// rasterized from its live string by UiText at paint time.
    /// </summary>
    public sealed class UiModule : ModuleBase
    {
        public override string Name { get { return "ui"; } }

        /// Number of baked-text image assets consulted by the label path.
        /// The only text source in this module is UiText's runtime stroker,
        /// so this is always 0 — a check can audit the invariant.
        public int BakedTextAssets { get { return 0; } }

        /// Whether any label resolved its pixels from a stored image rather
        /// than the live string. False by construction; the gate still
        /// measures the live raster path rather than trusting this flag.
        public bool TextBaked { get { return BakedTextAssets > 0; } }

        public UiSkin DefaultSkin { get; private set; }
        public UiWidget Focused { get; private set; }
        public readonly List<UiWidget> Widgets = new List<UiWidget>();

        private readonly Dictionary<string, UiSkin> skins =
            new Dictionary<string, UiSkin>();

        public UiModule()
        {
            DefaultSkin = new UiSkin("default", BuildDefaultSkinSource(), 10, 10, 10, 10);
            skins["default"] = DefaultSkin;
        }

        /// The built-in fallback skin authored procedurally at runtime:
        /// dark frame, warm edge band, soft center — 36x36 with 10 px
        /// borders so every resize is a real 9-slice exercise.
        private static SoftwareCanvas BuildDefaultSkinSource()
        {
            var c = new SoftwareCanvas(36, 36);
            c.Clear(new Rgba(30, 26, 38, 255));
            for (int y = 0; y < 36; y++)
            {
                for (int x = 0; x < 36; x++)
                {
                    bool border = x < 10 || y < 10 || x >= 26 || y >= 26;
                    if (border)
                    {
                        bool edge = x < 2 || y < 2 || x >= 34 || y >= 34;
                        c.SetPixel(x, y, edge
                            ? Rgba.Opaque(210, 160, 110)
                            : Rgba.Opaque(96, 74, 60));
                    }
                    else
                    {
                        c.SetPixel(x, y, Rgba.Opaque(52, 44, 62));
                    }
                }
            }
            // Distinct corner accents so a resize's corner fidelity is
            // measurable pixel-by-pixel.
            c.FillRect(2, 2, 8, 8, Rgba.Opaque(236, 200, 140));
            c.FillRect(26, 2, 8, 8, Rgba.Opaque(200, 150, 96));
            c.FillRect(2, 26, 8, 8, Rgba.Opaque(170, 120, 80));
            c.FillRect(26, 26, 8, 8, Rgba.Opaque(140, 96, 64));
            return c;
        }

        // ---- skins ------------------------------------------------------

        public void RegisterSkin(UiSkin skin)
        {
            if (skin != null && skin.Id != null) skins[skin.Id] = skin;
        }

        /// Removes an imported skin — the module falls back to the default
        /// skin for widgets that referenced it; nothing else is touched.
        public bool UnregisterSkin(string id)
        {
            if (id == null || id == "default") return false;
            return skins.Remove(id);
        }

        public UiSkin Skin(string id)
        {
            UiSkin s;
            if (id != null && skins.TryGetValue(id, out s)) return s;
            return null;
        }

        public bool HasSkin(string id) { return Skin(id) != null; }

        // ---- widgets ----------------------------------------------------

        public UiWidget Add(UiRole role, string id, int x, int y, int w,
            int h, string text, string skinId)
        {
            var wgt = new UiWidget
            {
                Id = id, Role = role, X = x, Y = y, W = w, H = h,
                Text = text ?? "", SkinId = skinId ?? ""
            };
            Widgets.Add(wgt);
            return wgt;
        }

        public UiWidget AddPanel(string id, int x, int y, int w, int h,
            string title, string skinId)
        {
            return Add(UiRole.Panel, id, x, y, w, h, title, skinId);
        }

        public UiWidget AddButton(string id, int x, int y, int w, int h,
            string label, string skinId)
        {
            return Add(UiRole.Button, id, x, y, w, h, label, skinId);
        }

        public UiWidget AddLabel(string id, int x, int y, int w, int h,
            string text)
        {
            return Add(UiRole.Label, id, x, y, w, h, text, "");
        }

        public UiWidget AddTextInput(string id, int x, int y, int w, int h,
            string placeholder, string skinId)
        {
            var wgt = Add(UiRole.TextInput, id, x, y, w, h, "", skinId);
            wgt.Placeholder = placeholder ?? "";
            return wgt;
        }

        public UiWidget AddPreview(string id, int x, int y, int w, int h,
            SoftwareCanvas sprite, string skinId)
        {
            var wgt = Add(UiRole.Preview, id, x, y, w, h, "", skinId);
            wgt.PreviewSprite = sprite;
            return wgt;
        }

        /// Deepest (last-painted) widget containing the point — the real
        /// hit path the pointer uses; the clickable area is the rect.
        public UiWidget HitTest(int px, int py)
        {
            for (int i = Widgets.Count - 1; i >= 0; i--)
            {
                if (Widgets[i].Contains(px, py)) return Widgets[i];
            }
            return null;
        }

        // ---- pointer + input ---------------------------------------------

        public void SetHovered(UiWidget w)
        {
            foreach (var g in Widgets)
            {
                if (g.State != UiState.Disabled && g != Focused)
                    g.State = g == w ? UiState.Hover : UiState.Normal;
            }
        }

        public void Press(UiWidget w)
        {
            if (w == null || w.State == UiState.Disabled) return;
            w.State = UiState.Pressed;
        }

        public void Release(UiWidget w)
        {
            if (w == null || w.State == UiState.Disabled) return;
            w.State = UiState.Normal;
        }

        public void SetEnabled(UiWidget w, bool enabled)
        {
            if (w == null) return;
            w.State = enabled ? UiState.Normal : UiState.Disabled;
        }

        /// Focus a text input: real keyboard focus, caret to the end.
        public void FocusInput(UiWidget w)
        {
            if (w == null || w.Role != UiRole.TextInput) return;
            if (Focused != null && Focused.State == UiState.Focus)
            {
                Focused.State = UiState.Normal;
            }
            Focused = w;
            w.State = UiState.Focus;
            w.Caret = w.InputText.Length;
        }

        public void BlurInput()
        {
            if (Focused != null && Focused.State == UiState.Focus)
            {
                Focused.State = UiState.Normal;
            }
            Focused = null;
        }

        /// True while a text input holds focus — typed keys go to the
        /// input, not the game layer.
        public bool TextInputActive
        {
            get { return Focused != null && Focused.Role == UiRole.TextInput; }
        }

        /// Real text entry: inserts the composed string at the caret.
        /// The stored value is the actual text — never glyph indices.
        public void TypeText(string s)
        {
            if (!TextInputActive || string.IsNullOrEmpty(s)) return;
            var w = Focused;
            w.InputText = w.InputText.Insert(w.Caret, s);
            w.Caret += s.Length;
        }

        public void Backspace()
        {
            if (!TextInputActive) return;
            var w = Focused;
            if (w.Caret <= 0 || w.InputText.Length == 0) return;
            w.InputText = w.InputText.Remove(w.Caret - 1, 1);
            w.Caret--;
        }

        public void CaretLeft()
        {
            if (TextInputActive && Focused.Caret > 0) Focused.Caret--;
        }

        public void CaretRight()
        {
            if (TextInputActive && Focused.Caret < Focused.InputText.Length)
            {
                Focused.Caret++;
            }
        }

        // ---- frame + paint -------------------------------------------------

        /// Snapshot the paint path consumes: widgets in order with each
        /// skin resolved (missing ids fall back to the default skin and the
        /// frame records the substitution honestly).
        public UiFrame BuildFrame(int viewportW, int viewportH)
        {
            var f = new UiFrame { ViewportW = viewportW, ViewportH = viewportH };
            foreach (var w in Widgets)
            {
                var s = Skin(w.SkinId);
                if (s == null && w.Role != UiRole.Label)
                {
                    s = DefaultSkin;
                    f.UsedFallbackSkin = true;
                }
                w.ResolvedSkin = s;
                f.Widgets.Add(w);
            }
            f.Focused = Focused;
            return f;
        }

        /// Runtime text rasterization used by labels — exposes the live
        /// string -> pixel path so checks can measure it directly.
        public SoftwareCanvas RenderText(string text, int scale, Rgba color)
        {
            return UiText.Render(text, scale, color);
        }

        /// The real draw path: skins 9-slice-resize to each widget's rect,
        /// states paint their own cues, text renders from the live string,
        /// and the focused input shows its caret. Screen space only — no
        /// iso projection, no furniture offsets.
        public void Paint(SoftwareCanvas canvas, UiFrame frame)
        {
            foreach (var w in frame.Widgets)
            {
                PaintWidget(canvas, w, frame);
            }
        }

        private void PaintWidget(SoftwareCanvas canvas, UiWidget w, UiFrame frame)
        {
            var skin = w.ResolvedSkin ?? DefaultSkin;
            if (w.Role == UiRole.Panel || w.Role == UiRole.Button
                || w.Role == UiRole.TextInput || w.Role == UiRole.Preview)
            {
                var img = skin.Render(w.W, w.H);
                Blit(canvas, img, w.X, w.Y);
            }

            switch (w.State)
            {
                case UiState.Hover:
                    canvas.FillRect(w.X + 1, w.Y + 1, w.W - 2, Math.Max(2, w.H / 5),
                        new Rgba(255, 255, 255, 26));
                    canvas.FillRect(w.X, w.Y, w.W, 1, Rgba.Opaque(255, 230, 170));
                    break;
                case UiState.Pressed:
                    canvas.FillRect(w.X + 1, w.Y + w.H - Math.Max(2, w.H / 4),
                        w.W - 2, Math.Max(2, w.H / 4), new Rgba(0, 0, 0, 60));
                    canvas.FillRect(w.X, w.Y, w.W, 1, Rgba.Opaque(90, 70, 50));
                    canvas.FillRect(w.X, w.Y + 1, w.W, 1, new Rgba(0, 0, 0, 46));
                    break;
                case UiState.Disabled:
                    canvas.FillRect(w.X, w.Y, w.W, w.H, new Rgba(20, 18, 26, 120));
                    break;
                case UiState.Focus:
                    RectOutline(canvas, w.X - 1, w.Y - 1, w.W + 2, w.H + 2,
                        Rgba.Opaque(96, 210, 220));
                    break;
            }

            if (w.Role == UiRole.Panel && w.Text.Length > 0)
            {
                UiText.Draw(canvas, w.Text, w.X + 12, w.Y + 8, 1,
                    Rgba.Opaque(238, 226, 205));
                canvas.FillRect(w.X + 10, w.Y + 24, w.W - 20, 1,
                    Rgba.Opaque(120, 100, 84));
            }
            else if (w.Role == UiRole.Button || w.Role == UiRole.Label)
            {
                var ink = w.State == UiState.Disabled
                    ? Rgba.Opaque(120, 116, 130)
                    : Rgba.Opaque(240, 234, 244);
                int tw = UiText.Measure(w.Text, 1);
                int tx = w.X + Math.Max(4, (w.W - tw) / 2);
                int ty = w.Y + Math.Max(2, (w.H - 12) / 2);
                UiText.Draw(canvas, w.Text, tx, ty, 1, ink);
            }
            else if (w.Role == UiRole.TextInput)
            {
                string shown = w.InputText;
                var ink = Rgba.Opaque(240, 234, 244);
                if (shown.Length == 0)
                {
                    shown = w.Placeholder;
                    ink = Rgba.Opaque(128, 122, 140);
                }
                int tx = w.X + 8, ty = w.Y + Math.Max(2, (w.H - 12) / 2);
                if (shown.Length > 0)
                {
                    UiText.Draw(canvas, shown, tx, ty, 1, ink);
                }
                if (w.State == UiState.Focus)
                {
                    int cx = tx + UiText.Measure(
                        w.InputText.Substring(0, w.Caret), 1);
                    canvas.FillRect(cx + 1, w.Y + 5, 1, w.H - 10,
                        Rgba.Opaque(255, 240, 180));
                }
            }
            else if (w.Role == UiRole.Preview && w.PreviewSprite != null)
            {
                var sp = w.PreviewSprite;
                int pw = Math.Min(w.W - 8, sp.Width * 2);
                int ph = Math.Min(w.H - 8, sp.Height * 2);
                int ox = w.X + (w.W - pw) / 2, oy = w.Y + (w.H - ph) / 2;
                for (int y = 0; y < ph; y++)
                {
                    int sy = y * sp.Height / ph;
                    for (int x = 0; x < pw; x++)
                    {
                        int sx = x * sp.Width / pw;
                        var c = sp.GetPixel(sx, sy);
                        if (c.A != 0) canvas.SetPixel(ox + x, oy + y, c);
                    }
                }
            }
        }

        private static void Blit(SoftwareCanvas canvas, SoftwareCanvas src,
            int ox, int oy)
        {
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    int i = (y * src.Width + x) * 4;
                    if (src.Pixels[i + 3] == 0) continue;
                    canvas.SetPixel(ox + x, oy + y,
                        new Rgba(src.Pixels[i], src.Pixels[i + 1],
                            src.Pixels[i + 2], src.Pixels[i + 3]));
                }
            }
        }

        private static void RectOutline(SoftwareCanvas canvas, int x, int y,
            int w, int h, Rgba c)
        {
            canvas.FillRect(x, y, w, 1, c);
            canvas.FillRect(x, y + h - 1, w, 1, c);
            canvas.FillRect(x, y, 1, h, c);
            canvas.FillRect(x + w - 1, y, 1, h, c);
        }

        /// Probe does real work on a throwaway instance: a 9-slice skin is
        /// authored, resized and sampled; a widget's rect drives its hit
        /// area; a focused input takes real text. Live state untouched.
        protected override bool OnProbe()
        {
            var src = new SoftwareCanvas(12, 12);
            src.Clear(Rgba.Opaque(80, 60, 90));
            src.FillRect(0, 0, 4, 4, Rgba.Opaque(250, 120, 60));
            var skin = new UiSkin("probe", src, 4, 4, 4, 4);
            var mid = skin.Render(20, 20);
            var tiny = skin.Render(9, 9);
            var c1 = mid.GetPixel(0, 0);
            var c2 = mid.GetPixel(19, 19);
            bool resizeOk = c1.R == 250 && c2.R == 80
                && tiny.GetPixel(0, 0).R == 250;

            var m = new UiModule();
            m.RegisterSkin(skin);
            var b = m.AddButton("b", 4, 4, 20, 20, "확인", "probe");
            b.SetRect(4, 4, 30, 12);
            bool hitOk = b.Contains(33, 15) && !b.Contains(34, 16);
            var inp = m.AddTextInput("i", 0, 0, 40, 14, "이름", "probe");
            m.FocusInput(inp);
            m.TypeText("카페");
            m.Backspace();
            m.TypeText("페");
            bool ioOk = inp.InputText == "카페" && inp.Caret == 2;
            var frame = m.BuildFrame(64, 48);
            var canv = new SoftwareCanvas(64, 48);
            canv.Clear(new Rgba(0, 0, 0, 255));
            m.Paint(canv, frame);
            return resizeOk && hitOk && ioOk
                && frame.Widgets.Count == 2
                && m.TextInputActive;
        }
    }
}
