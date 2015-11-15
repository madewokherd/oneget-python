using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PythonProvider
{
    // Based on PEP 0496
    enum EnvironmentMarkerVariable
    {
        os_name,
        sys_platform,
        platform_release,
        implementation_name,
        platform_machine,
        platform_python_implementation,
        // version variables:
        python_version,
        python_full_version,
        platform_version,
        implementation_version,
    }

    struct MarkerToken
    {
        public string token;
        public bool is_literal;
        public int position;

        public MarkerToken(string token, bool is_literal, int position)
        {
            this.token = token;
            this.is_literal = is_literal;
            this.position = position;
        }

        public bool EqualsToken(string token)
        {
            if (is_literal) return false;
            return this.token == token;
        }
    }

    class EnvironmentMarker
    {
        private static List<MarkerToken> TokenizeMarker(string marker)
        {
            var result = new List<MarkerToken>();

            int pos = 0;

            while (pos < marker.Length)
            {
                int start_pos = pos;

                if (marker[pos] == ' ' || marker[pos] == '\t' || marker[pos] == '\f')
                {
                    pos++;
                    continue;
                }

                if (char.IsLetter(marker[pos]) || marker[pos] == '_')
                {
                    // an identifier or keyword
                    do
                    {
                        pos++;
                    } while (pos < marker.Length && (char.IsLetterOrDigit(marker[pos]) || marker[pos] == '_'));

                    // FIXME: account for string literals with prefixes?

                    result.Add(new MarkerToken(marker.Substring(start_pos, pos - start_pos), false, start_pos));
                    continue;
                }

                if (marker[pos] == '"' || marker[pos] == '\'')
                {
                    // a string literal
                    char quot = marker[pos];
                    bool triple_quoted;

                    if (pos + 2 < marker.Length && marker[pos + 1] == quot && marker[pos + 2] == quot)
                    {
                        triple_quoted = true;
                        pos += 3;
                    }
                    else
                    {
                        triple_quoted = false;
                        pos += 1;
                    }

                    var sb = new StringBuilder();

                    while (true)
                    {
                        if (pos >= marker.Length)
                            throw new ArgumentException(string.Format("EOL while scanning string literal in marker {0}", marker));

                        if (marker[pos] == quot &&
                            (!triple_quoted ||
                             (pos + 2 < marker.Length && marker[pos + 1] == quot && marker[pos + 2] == quot)))
                        {
                            if (triple_quoted)
                                pos += 3;
                            else
                                pos += 1;
                            break;
                        }

                        if (marker[pos] == '\\')
                        {
                            pos++;
                            if (pos >= marker.Length)
                                throw new ArgumentException(string.Format("EOL while scanning string literal in marker {0}", marker));

                            int bass, min, max;

                            switch (marker[pos])
                            {
                                case '\\':
                                case '\'':
                                case '"':
                                    sb.Append(marker[pos]);
                                    pos++;
                                    continue;
                                case 'a':
                                    sb.Append('\a');
                                    pos++;
                                    continue;
                                case 'b':
                                    sb.Append('\b');
                                    pos++;
                                    continue;
                                case 'f':
                                    sb.Append('\f');
                                    pos++;
                                    continue;
                                case 'n':
                                    sb.Append('\n');
                                    pos++;
                                    continue;
                                case 'r':
                                    sb.Append('\r');
                                    pos++;
                                    continue;
                                case 'v':
                                    sb.Append('\v');
                                    pos++;
                                    continue;
                                case 'u':
                                    pos++;
                                    bass = 16;
                                    min = max = 4;
                                    break;
                                case 'U':
                                    pos++;
                                    bass = 16;
                                    min = max = 8;
                                    break;
                                case '0':
                                case '1':
                                case '2':
                                case '3':
                                case '4':
                                case '5':
                                case '6':
                                case '7':
                                    bass = 8;
                                    min = 1;
                                    max = 3;
                                    break;
                                case 'x':
                                    pos++;
                                    bass = 16;
                                    min = max = 2;
                                    break;
                                case 'N':
                                    throw new NotImplementedException("\\N string escape not implemented");
                                default:
                                    sb.Append("\\");
                                    sb.Append(marker[pos]);
                                    pos++;
                                    continue;
                            }

                            // numeric character escape
                            int chars_read = 0, total = 0;
                            while (chars_read < max && pos < marker.Length)
                            {
                                int val;
                                if (marker[pos] >= '0' && marker[pos] <= '9')
                                    val = marker[pos] - '0';
                                else if (marker[pos] >= 'a' && marker[pos] <= 'f')
                                    val = marker[pos] - 'a';
                                else if (marker[pos] >= 'A' && marker[pos] <= 'F')
                                    val = marker[pos] - 'A';
                                else
                                    val = 16;

                                if (val >= bass)
                                    break;

                                total = total * bass + val;
                                pos++;
                                chars_read++;
                            }

                            if (chars_read < min)
                                throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));

                            sb.Append((char)total);
                            continue;
                        }

                        sb.Append(marker[pos]);
                        pos++;
                    }

                    result.Add(new MarkerToken(sb.ToString(), true, start_pos));
                    continue;
                }

                // else look for an operator/delimiter
                string token;
                switch (marker[pos])
                {
                    case '(':
                        token = "(";
                        break;
                    case ')':
                        token = ")";
                        break;
                    case '=':
                        pos++;
                        if (pos >= marker.Length)
                            throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));
                        switch (marker[pos])
                        {
                            case '=':
                                token = "==";
                                break;
                            default:
                                throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));
                        }
                        break;
                    case '!':
                        pos++;
                        if (pos >= marker.Length)
                            throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));
                        switch (marker[pos])
                        {
                            case '=':
                                token = "!=";
                                break;
                            default:
                                throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));
                        }
                        break;
                    case '<':
                        pos++;
                        if (pos >= marker.Length)
                        {
                            token = "<";
                            pos--;
                            break;
                        }
                        switch (marker[pos])
                        {
                            case '=':
                                token = "<=";
                                break;
                            default:
                                token = "<";
                                pos--;
                                break;
                        }
                        break;
                    case '>':
                        pos++;
                        if (pos >= marker.Length)
                        {
                            token = ">";
                            pos--;
                            break;
                        }
                        switch (marker[pos])
                        {
                            case '=':
                                token = ">=";
                                break;
                            default:
                                token = ">";
                                pos--;
                                break;
                        }
                        break;
                    default:
                        throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", pos, marker));
                }

                pos++;
                result.Add(new MarkerToken(token, false, start_pos));
            }

            return result;
        }

        private enum MarkerType
        {
            And,
            Or,
            StringLiteral,
            StringVariable,
            VersionVariable,
            ExtraVariable,
            ComparisonList,
        }

        private enum ComparisonType
        {
            Equal,
            NotEqual,
            In,
            NotIn,
            LT,
            GT,
            LTE,
            GTE
        }

        private static Dictionary<string, ComparisonType> operators;

        static EnvironmentMarker()
        {
            operators = new Dictionary<string, ComparisonType>();
            operators["=="] = ComparisonType.Equal;
            operators["!="] = ComparisonType.NotEqual;
            operators["in"] = ComparisonType.In;
            operators["<"] = ComparisonType.LT;
            operators[">"] = ComparisonType.GT;
            operators["<="] = ComparisonType.LTE;
            operators[">="] = ComparisonType.GTE;
        }

        private EnvironmentMarker[] submarkers;
        private MarkerType type;
        private string str_value;
        private ComparisonType[] comparisons;
        private EnvironmentMarkerVariable marker_variable;

        private EnvironmentMarker()
        { }

        public static EnvironmentMarker ParseEnvironmentMarker(string marker)
        {
            var tokens = TokenizeMarker(marker).ToArray();

            int start = 0;
            int end = tokens.Length;

            return ParseOrList(tokens, start, end, marker);
        }

        private static EnvironmentMarker ParseOrList(MarkerToken[] tokens, int start, int end, string marker)
        {
            int level = 0;
            int i;
            var or_indices = new List<int>();

            for (i = start; i < end; i++)
            {
                var token = tokens[i];
                if (token.EqualsToken("("))
                    level++;
                else if (token.EqualsToken(")"))
                {
                    level--;
                    if (level < 0)
                        throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", token.position, marker));
                }

                if (level == 0 && token.EqualsToken("or"))
                    or_indices.Add(i);
            }

            if (level != 0)
                throw new ArgumentException(string.Format("unclosed ( in marker {0}", marker));

            if (or_indices.Count == 0)
                return ParseAndList(tokens, start, end, marker);

            var result = new EnvironmentMarker();
            result.submarkers = new EnvironmentMarker[or_indices.Count + 1];
            result.submarkers[0] = ParseAndList(tokens, start, or_indices[0], marker);
            for (i = 0; i < or_indices.Count - 1; i++)
            {
                result.submarkers[i + 1] = ParseAndList(tokens, or_indices[i] + 1, or_indices[i + 1], marker);
            }
            result.submarkers[or_indices.Count] = ParseAndList(tokens, or_indices[or_indices.Count-1] + 1, end, marker);
            result.type = MarkerType.Or;
            return result;
        }

        private static EnvironmentMarker ParseAndList(MarkerToken[] tokens, int start, int end, string marker)
        {
            int level = 0;
            int i;
            var and_indices = new List<int>();

            for (i = start; i < end; i++)
            {
                var token = tokens[i];
                if (token.EqualsToken("("))
                    level++;
                else if (token.EqualsToken(")"))
                {
                    level--;
                    if (level < 0)
                        throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", token.position, marker));
                }

                if (level == 0 && token.EqualsToken("and"))
                    and_indices.Add(i);
            }

            if (level != 0)
                throw new ArgumentException(string.Format("unclosed ( in marker {0}", marker));

            if (and_indices.Count == 0)
                return ParseComparisonList(tokens, start, end, marker);

            var result = new EnvironmentMarker();
            result.submarkers = new EnvironmentMarker[and_indices.Count + 1];
            result.submarkers[0] = ParseComparisonList(tokens, start, and_indices[0], marker);
            for (i = 0; i < and_indices.Count - 1; i++)
            {
                result.submarkers[i + 1] = ParseComparisonList(tokens, and_indices[i] + 1, and_indices[i + 1], marker);
            }
            result.submarkers[and_indices.Count] = ParseComparisonList(tokens, and_indices[and_indices.Count - 1] + 1, end, marker);
            result.type = MarkerType.And;
            return result;
        }

        private static EnvironmentMarker ParseComparisonList(MarkerToken[] tokens, int start, int end, string marker)
        {
            int level = 0;
            int i;
            var comparison_indices = new List<int>();
            var comparison_next_indices = new List<int>();
            var comparison_types = new List<ComparisonType>();

            for (i = start; i < end; i++)
            {
                var token = tokens[i];
                if (token.EqualsToken("("))
                    level++;
                else if (token.EqualsToken(")"))
                {
                    level--;
                    if (level < 0)
                        throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", token.position, marker));
                }

                if (level == 0)
                {
                    if (!token.is_literal && operators.ContainsKey(token.token))
                    {
                        comparison_indices.Add(i);
                        comparison_next_indices.Add(i + 1);
                        comparison_types.Add(operators[token.token]);
                    }
                    else if (i + 1 < end && token.EqualsToken("not") && tokens[i+1].EqualsToken("in"))
                    {
                        comparison_indices.Add(i);
                        comparison_next_indices.Add(i + 2);
                        i++;
                        comparison_types.Add(ComparisonType.NotIn);
                    }
                }
            }

            if (level != 0)
                throw new ArgumentException(string.Format("unclosed ( in marker {0}", marker));

            if (comparison_indices.Count == 0)
                return ParseAtom(tokens, start, end, marker);
            
            var result = new EnvironmentMarker();
            result.submarkers = new EnvironmentMarker[comparison_indices.Count + 1];
            result.submarkers[0] = ParseAtom(tokens, start, comparison_indices[0], marker);
            for (i = 0; i < comparison_indices.Count - 1; i++)
            {
                result.submarkers[i + 1] = ParseAtom(tokens, comparison_next_indices[i], comparison_indices[i + 1], marker);
            }
            result.submarkers[comparison_indices.Count] = ParseAtom(tokens, comparison_next_indices[comparison_indices.Count - 1], end, marker);
            result.type = MarkerType.ComparisonList;
            result.comparisons = comparison_types.ToArray();
            return result;
        }

        private static EnvironmentMarker ParseAtom(MarkerToken[] tokens, int start, int end, string marker)
        {
            if (tokens.Length == 0)
                throw new ArgumentException(string.Format("marker is empty: {1}", marker));

            var token = tokens[start];

            if (end <= start)
                throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", token.position, marker));

            if (token.EqualsToken("("))
            {
                int level = 1;
                int i;

                for (i = start+1; i < end; i++)
                {
                    if (tokens[i].EqualsToken("("))
                        level++;
                    else if (tokens[i].EqualsToken(")"))
                    {
                        level--;
                        if (level == 0 && i < end - 1)
                            throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", tokens[i+1].position, marker));
                    }
                }

                if (level != 0)
                    throw new ArgumentException(string.Format("unclosed ( in marker {0}", marker));

                return ParseOrList(tokens, start + 1, end - 1, marker);
            }

            if (end != start + 1)
                throw new ArgumentException(string.Format("invalid syntax at character {0} in marker {1}", tokens[start + 1].position, marker));

            var result = new EnvironmentMarker();

            if (token.is_literal)
                result.type = MarkerType.StringLiteral;
            else if (token.token == "extra")
                result.type = MarkerType.ExtraVariable;
            else
            {
                result.marker_variable = (EnvironmentMarkerVariable)Enum.Parse(typeof(EnvironmentMarkerVariable), token.token);
                if (result.marker_variable >= EnvironmentMarkerVariable.python_version)
                    result.type = MarkerType.VersionVariable;
                else
                    result.type = MarkerType.StringVariable;
            }

            result.str_value = token.token;

            return result;
        }

        private bool ValueIsTrue(object obj)
        {
            // Possible types: bool, string, VersionIdentifier
            if (obj is bool)
                return (bool)obj;
            if (obj is string)
                return (string)obj != "";
            if (obj is VersionIdentifier)
                return ((VersionIdentifier)obj).raw_version_string != "";
            throw new Exception("should not be reached");
        }

        private object RealEval(PythonInstall install)
        {
            object result = null;
            int i;
            switch (type)
            {
                case MarkerType.And:
                    result = submarkers[0].RealEval(install);
                    i = 1;
                    while (i < submarkers.Length && ValueIsTrue(result))
                        result = submarkers[i++].RealEval(install);
                    break;
                case MarkerType.Or:
                    result = submarkers[0].RealEval(install);
                    i = 1;
                    while (i < submarkers.Length && !ValueIsTrue(result))
                        result = submarkers[i++].RealEval(install);
                    break;
                case MarkerType.StringLiteral:
                    result = str_value;
                    break;
                case MarkerType.StringVariable:
                    result = install.get_string_marker_variable(marker_variable);
                    break;
                case MarkerType.VersionVariable:
                    result = install.get_version_marker_variable(marker_variable);
                    break;
                case MarkerType.ExtraVariable:
                    result = ""; // FIXME: add support for specifying extras?
                    break;
                case MarkerType.ComparisonList:
                    object a, b;
                    a = submarkers[0].RealEval(install);
                    if (a is bool)
                        throw new Exception("can't do comparisons with booleans");
                    bool res = true, version_comparison;
                    i = 0;
                    while (res && i < comparisons.Length)
                    {
                        b = submarkers[i + 1].RealEval(install);
                        if (b is bool)
                            throw new Exception("can't do comparisons with booleans");
                        var cmp = comparisons[i];
                        if (cmp == ComparisonType.In || cmp == ComparisonType.NotIn)
                            version_comparison = false;
                        else if (cmp == ComparisonType.Equal || cmp == ComparisonType.NotEqual)
                            version_comparison = (a is VersionIdentifier || b is VersionIdentifier);
                        else
                            version_comparison = true;
                        if (version_comparison)
                        {
                            VersionIdentifier va, vb;
                            va = (a as VersionIdentifier) ?? new VersionIdentifier((string)a);
                            vb = (b as VersionIdentifier) ?? new VersionIdentifier((string)b);
                            switch (cmp)
                            {
                                case ComparisonType.Equal:
                                    res = va.Compare(vb) == 0;
                                    break;
                                case ComparisonType.NotEqual:
                                    res = va.Compare(vb) != 0;
                                    break;
                                case ComparisonType.LT:
                                    res = va.Compare(vb) < 0;
                                    break;
                                case ComparisonType.GT:
                                    res = va.Compare(vb) > 0;
                                    break;
                                case ComparisonType.LTE:
                                    res = va.Compare(vb) <= 0;
                                    break;
                                case ComparisonType.GTE:
                                    res = va.Compare(vb) >= 0;
                                    break;
                            }
                        }
                        else
                        {
                            string sa, sb;
                            sa = (a as string) ?? ((VersionIdentifier)a).raw_version_string;
                            sb = (b as string) ?? ((VersionIdentifier)b).raw_version_string;
                            switch (cmp)
                            {
                                case ComparisonType.Equal:
                                    res = sa == sb;
                                    break;
                                case ComparisonType.NotEqual:
                                    res = sa != sb;
                                    break;
                                case ComparisonType.In:
                                    res = sb.Contains(sa);
                                    break;
                                case ComparisonType.NotIn:
                                    res = !sb.Contains(sa);
                                    break;
                            }
                        }
                        a = b;
                        i++;
                    }
                    result = res;
                    break;
            }

            return result;
        }

        internal bool Eval(PythonInstall install)
        {
            return ValueIsTrue(RealEval(install));
        }
    }
}
