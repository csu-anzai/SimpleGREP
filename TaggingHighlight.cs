using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using Highlight.Patterns;

namespace grepcmd
{
    public class TaggingHighlight : Highlight.Engines.Engine
    {
        public List<Color> coloridx = new List<Color>();
        public Dictionary<ColorPair, int> stylemap = new Dictionary<ColorPair, int>();
        public List<int> spanlookbak = new List<int>();
        public List<(int st, int ed, int style)> spans = new List<(int st, int ed, int style)>();

        private void TagStyle(ColorPair style, Capture match)
        {
            if (!stylemap.ContainsKey(style))
            {
                var r = style.ForeColor;
                var mc = Color.FromArgb(r.A, r.R, r.G, r.B);
                coloridx.Add(mc);
                int c = coloridx.Count - 1;
                stylemap.Add(style, c);
            }
            int stp, edp;
            spans.Add((stp = match.Index, edp = match.Length + match.Index, stylemap[style]));
            while (spanlookbak.Count < edp) spanlookbak.Add(-1);
            for (var i = stp; i < edp; i++)
                spanlookbak[i] = spans.Count - 1;
        }
        protected override string ProcessBlockPatternMatch(Definition definition, BlockPattern pattern, Match match)
        {
            TagStyle(pattern.Style.Colors, match);
            return match.Value;
        }

        protected override string ProcessMarkupPatternMatch(Definition definition, MarkupPattern pattern, Match match)
        {
            TagStyle(pattern.BracketColors, match.Groups["openTag"]);
            TagStyle(pattern.Style.Colors, match.Groups["tagMame"]);
            ColorPair attributeNameStyle = null, attributeValueStyle = null;
            if (pattern.HighlightAttributes)
            {
                attributeNameStyle = pattern.AttributeNameColors;
                attributeValueStyle = pattern.AttributeValueColors;
            }
            if (attributeNameStyle != null)
            {
                for (var i = 0; i < match.Groups["attribute"].Captures.Count; i++)
                {
                    TagStyle(attributeNameStyle, match.Groups["attribName"].Captures[i]);

                    if (String.IsNullOrWhiteSpace(match.Groups["attribValue"].Captures[i].Value))
                    {
                        continue;
                    }

                    TagStyle(attributeValueStyle, match.Groups["attribValue"].Captures[i]);
                }
            }
            TagStyle(pattern.BracketColors, match.Groups["closeTag"]);

            return match.Value;
        }

        protected override string ProcessWordPatternMatch(Definition definition, WordPattern pattern, Match match)
        {
            TagStyle(pattern.Style.Colors, match);
            return match.Value;
        }
    }
}
