using System.Collections.Generic;
using System.IO;
using System.Text;
using HandlebarsDotNet;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis
{
    public class MarkdownEncoder : ITextEncoder
    {
        public void Encode(StringBuilder text, TextWriter target)
        {
            if (text == null || text.Length == 0) return;

            EncodeImpl(text.ToString(), target);
        }

        public void Encode(string text, TextWriter target)
        {
            if (string.IsNullOrEmpty(text)) return;

            EncodeImpl(text, target);
        }

        public void Encode<T>(T text, TextWriter target) where T : IEnumerator<char>
        {
            if (text == null) return;

            EncodeImpl(text.ToString(), target);
        }

        private static void EncodeImpl<T>(T text, TextWriter target) where T : IEnumerable<char>
        {
            foreach (char value in text)
            {
                switch (value)
                {
                    case '"':
                        target.Write("&#34;");
                        break;
                    case '&':
                        target.Write("&#38;");
                        break;
                    case '<':
                        target.Write("&#60;");
                        break;
                    case '>':
                        target.Write("&#62;");
                        break;
                    case '#':
                        target.Write("&#35;");
                        break;
                    case '`':
                        target.Write("&#96;");
                        break;
                    case '_':
                        target.Write("&#95;");
                        break;
                    case '*':
                        target.Write("&#42;");
                        break;
                    case '[':
                        target.Write("&#91;");
                        break;
                    case ']':
                        target.Write("&#93;");
                        break;
                    case '(':
                        target.Write("&#40;");
                        break;
                    case ')':
                        target.Write("&#41;");
                        break;
                    case '\\':
                        target.Write("&#92;");
                        break;
                    default:
                        target.Write(value);
                        break;
                }
            }
        }
    }
}
