using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PythonProvider
{
    public class VersionIdentifier
    {
        // see: https://www.python.org/dev/peps/pep-0440/

        public int epoch;

        public int[] release;

        public bool is_valid;
        public string raw_version_string;

        public enum PrereleaseType
        {
            Alpha,
            Beta,
            ReleaseCandidate,
            Final
        }

        public PrereleaseType prerelease_type;
        public int prerelease_version;

        public bool is_postrelease;
        public int postrelease_version;

        public bool is_devrelease;
        public int devrelease_version;

        public bool is_localversion;
        public object[] localversion_segments;

        public bool is_wildcard;

        public VersionIdentifier(string version_string, bool allow_wildcard)
        {
            is_valid = true;
            raw_version_string = version_string.Trim();
            if (!ParseVersion(version_string, allow_wildcard))
            {
                is_valid = false;
                this.release = new int[] { 0 };
            }
        }

        public VersionIdentifier(string version_string): this(version_string, false)
        {
            is_valid = true;
        }

        private PrereleaseType GetPrereleaseType(string identifier)
        {
            switch (identifier)
            {
                case "a":
                case "alpha":
                    return PrereleaseType.Alpha;
                case "b":
                case "beta":
                    return PrereleaseType.Beta;
                case "c":
                case "rc":
                case "pre":
                case "preview":
                    return PrereleaseType.ReleaseCandidate;
                default:
                    return PrereleaseType.Final;
            }
        }

        public bool IsPrerelease
        {
            get
            {
                return prerelease_type != PrereleaseType.Final || is_devrelease;
            }
        }

        private bool ParseVersion(string version_string)
        {
            return ParseVersion(version_string, false);
        }

        private bool ParseVersion(string version_string, bool allow_wildcard)
        {
            int pos = 0;

            version_string = version_string.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(version_string))
                return false;

            // trim 'v' from the start
            if (version_string[0] == 'v' && version_string.Length >= 2)
            {
                version_string = version_string.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(version_string) || !char.IsDigit(version_string[0]))
                return false;

            // up to 1 epoch segment
            for (int i=1; i<version_string.Length; i++)
            {
                if (version_string[i] == '!')
                {
                    epoch = int.Parse(version_string.Substring(0, i));
                    pos = i + 1;
                    break;
                }
                else if (!char.IsDigit(version_string[i]))
                    break;
            }

            // release part
            LinkedList<int> release_components = new LinkedList<int>();

            // there must be at least one release component
            if (pos >= version_string.Length || !char.IsDigit(version_string[pos]))
            {
                return false;
            }

            int release_component = 0;
            do
            {
                release_component = release_component * 10 + version_string[pos] - '0';
                pos++;
            } while (pos < version_string.Length && char.IsDigit(version_string[pos]));

            release_components.AddLast(release_component);

            // any number of additional release components
            while (pos < version_string.Length - 1 && version_string[pos] == '.' &&
                char.IsDigit(version_string[pos+1]))
            {
                pos++;
                release_component = 0;
                do
                {
                    release_component = release_component * 10 + version_string[pos] - '0';
                    pos++;
                } while (pos < version_string.Length && char.IsDigit(version_string[pos]));
                release_components.AddLast(release_component);
            }

            release = release_components.ToArray();

            // maybe a wildcard, if this is a version specifier
            if (pos < version_string.Length - 1 && version_string[pos] == '.' &&
                version_string[pos+1] == '*')
            {
                pos += 2;
                is_wildcard = true;
                goto end; // No other segments permitted after wildcard
            }

            // up to 1 prerelease segment
            if ((pos < version_string.Length && char.IsLetter(version_string[pos])) ||
                (pos + 1 < version_string.Length && 
                 (version_string[pos] == '.' || version_string[pos] == '-' || version_string[pos] == '_') &&
                 char.IsLetter(version_string[pos+1])))
            {
                int prev_pos = pos;

                if (!char.IsLetter(version_string[pos]))
                    pos++;

                int id_start = pos;

                while (pos < version_string.Length && char.IsLetter(version_string[pos]))
                {
                    pos++;
                }

                string prerelease_identifier = version_string.Substring(id_start, pos - id_start);
                prerelease_type = GetPrereleaseType(prerelease_identifier);
                if (prerelease_type == PrereleaseType.Final)
                {
                    // unknown prerelease identifier
                    pos = prev_pos;
                }
                else
                {
                    prerelease_version = 0;
                    if (pos + 1 < version_string.Length && 
                        (version_string[pos] == '-' || version_string[pos] == '.' || version_string[pos] == '_') &&
                        char.IsDigit(version_string[pos + 1]))
                        pos++;
                    while (pos < version_string.Length && char.IsDigit(version_string[pos]))
                    {
                        prerelease_version = prerelease_version * 10 + version_string[pos] - '0';
                        pos++;
                    }
                }
            }
            else
            {
                prerelease_type = PrereleaseType.Final;
                prerelease_version = 0;
            }

            // up to 1 post-release segment
            if (pos + 3 < version_string.Length &&
                (version_string.Substring(pos, 4) == "post" ||
                 (pos + 4 < version_string.Length &&
                  (version_string[pos] == '.' || version_string[pos] == '-' || version_string[pos] == '_') &&
                  version_string.Substring(pos+1, 4) == "post")))
            {
                if (version_string[pos] != 'p')
                    pos++;

                pos += 4;

                is_postrelease = true;

                postrelease_version = 0;
                if (pos + 1 < version_string.Length &&
                    (version_string[pos] == '-' || version_string[pos] == '.' || version_string[pos] == '_') &&
                    char.IsDigit(version_string[pos + 1]))
                    pos++;
                while (pos < version_string.Length && char.IsDigit(version_string[pos]))
                {
                    postrelease_version = postrelease_version * 10 + version_string[pos] - '0';
                    pos++;
                }
            }
            else if (pos < version_string.Length &&
                     (version_string[pos] == 'r' ||
                      (pos + 1 < version_string.Length && 
                       (version_string[pos] == '-' || version_string[pos] == '.' || version_string[pos] == '_') &&
                       version_string[pos+1] == 'r')))
            {
                // -r and -rev spellings
                if (version_string[pos] == '-' || version_string[pos] == '.' || version_string[pos] == '_')
                    pos++;

                if (version_string[pos] == 'r')
                    pos++;

                if (pos + 1 < version_string.Length &&
                    version_string[pos] == 'e' &&
                    version_string[pos+1] == 'v')
                    pos += 2;

                is_postrelease = true;

                postrelease_version = 0;
                if (pos + 1 < version_string.Length &&
                    (version_string[pos] == '-' || version_string[pos] == '.' || version_string[pos] == '_') &&
                    char.IsDigit(version_string[pos + 1]))
                    pos++;
                while (pos < version_string.Length && char.IsDigit(version_string[pos]))
                {
                    postrelease_version = postrelease_version * 10 + version_string[pos] - '0';
                    pos++;
                }
            }
            else if (pos + 1 < version_string.Length &&
                     version_string[pos] == '-' &&
                     char.IsDigit(version_string[pos+1]))
            {
                // -N spelling
                pos++;

                is_postrelease = true;

                postrelease_version = 0;
                while (pos < version_string.Length && char.IsDigit(version_string[pos]))
                {
                    postrelease_version = postrelease_version * 10 + version_string[pos] - '0';
                    pos++;
                }
            }

            // up to 1 dev-release segment
            if (pos + 2 < version_string.Length &&
                (version_string.Substring(pos, 3) == "dev" ||
                 (pos + 3 < version_string.Length &&
                  (version_string[pos] == '.' || version_string[pos] == '-' || version_string[pos] == '_') &&
                  version_string.Substring(pos+1, 3) == "dev")))
            {
                if (version_string[pos] != 'd')
                    pos++;

                pos += 3;

                is_devrelease = true;

                devrelease_version = 0;
                // PEP440 doesn't allow a separator here.
                while (pos < version_string.Length && char.IsDigit(version_string[pos]))
                {
                    devrelease_version = devrelease_version * 10 + version_string[pos] - '0';
                    pos++;
                }
            }

            // up to 1 local version segment
            if (pos + 1 < version_string.Length &&
                version_string[pos] == '+')
            {
                pos++;

                is_localversion = true;
                string localversion_label = version_string.Substring(pos).Replace('-', '.').Replace('_', '.');

                for (int i = 0; i < localversion_label.Length; i++)
                {
                    char ch = localversion_label[i];
                    if (!char.IsLetterOrDigit(ch) && ch != '.')
                        return false;
                }

                if (localversion_label[0] == '.')
                    return false;

                if (localversion_label[localversion_label.Length - 1] == '.')
                    return false;

                string[] string_segments = localversion_label.Split('.');

                localversion_segments = new object[string_segments.Length];

                for (int i = 0; i < localversion_segments.Length; i++)
                {
                    string segment = (string)string_segments[i];
                    bool is_int=true;
                    for (int j=0; j<segment.Length; j++)
                    {
                        if (!char.IsDigit(segment[j]))
                        {
                            is_int = false;
                            break;
                        }
                    }
                    if (is_int)
                        localversion_segments[i] = int.Parse(segment);
                    else
                        localversion_segments[i] = segment;
                }

                pos = version_string.Length;
            }

            end:

            if (pos != version_string.Length)
                return false;

            return true;
        }

        private int cmp(int a, int b)
        {
            if (a < b)
                return -1;
            else if (a == b)
                return 0;
            else
                return 1;
        }

        private int cmp(string a, string b)
        {
            return string.Compare(a, b, StringComparison.InvariantCulture);
        }

        private bool is_plain_devrelease()
        {
            return is_devrelease && prerelease_type == PrereleaseType.Final && !is_postrelease;
        }

        public int Compare(VersionIdentifier other)
        {
            return Compare(other, false, false, false);
        }

        public int Compare(VersionIdentifier other, bool ignore_local, bool ignore_pre, bool ignore_post)
        {
            int res = cmp(epoch, other.epoch);

            if (res != 0)
                return res;

            int common_segments = release.Length < other.release.Length ? release.Length : other.release.Length;

            for (int i=0; i<common_segments; i++)
            {
                res = cmp(release[i], other.release[i]);
                if (res != 0)
                    return res;
            }

            if (common_segments < release.Length)
            {
                for (int i = common_segments; i < release.Length; i++)
                    if (release[i] != 0)
                        return 1;
            }

            if (common_segments < other.release.Length)
            {
                for (int i = common_segments; i < other.release.Length; i++)
                    if (other.release[i] != 0)
                        return -1;
            }

            if (!ignore_pre)
            {
                if (is_plain_devrelease() && !other.is_plain_devrelease())
                    return -1;

                if (!is_plain_devrelease() && other.is_plain_devrelease())
                    return 1;

                res = cmp((int)prerelease_type, (int)other.prerelease_type);
                if (res != 0)
                    return res;

                res = cmp(prerelease_version, other.prerelease_version);
                if (res != 0)
                    return res;
            }

            if (!ignore_post)
            {
                if (is_postrelease && !other.is_postrelease)
                    return 1;

                if (!is_postrelease && other.is_postrelease)
                    return -1;

                res = cmp(postrelease_version, other.postrelease_version);
                if (res != 0)
                    return res;

                if (is_devrelease && !other.is_devrelease)
                    return -1;

                if (!is_devrelease && other.is_devrelease)
                    return 1;

                res = cmp(devrelease_version, other.devrelease_version);
                if (res != 0)
                    return res;
            }

            if (!ignore_local)
            {
                if (is_localversion && !other.is_localversion)
                    return 1;

                if (!is_localversion && other.is_localversion)
                    return -1;

                if (is_localversion)
                {
                    common_segments = localversion_segments.Length < other.localversion_segments.Length ? localversion_segments.Length : other.localversion_segments.Length;

                    for (int i = 0; i < common_segments; i++)
                    {
                        string this_str = localversion_segments[i] as string;
                        if (this_str != null)
                        {
                            string other_str = other.localversion_segments[i] as string;
                            if (other_str == null)
                                // any string < any int
                                return -1;
                            else
                            {
                                res = cmp(this_str, other_str);
                                if (res != 0)
                                    return res;
                            }
                        }
                        else
                        {
                            int this_int = (int)localversion_segments[i];
                            string other_string = other.localversion_segments[i] as string;
                            if (other_string != null)
                                // any int > any string
                                return 1;
                            else
                            {
                                res = cmp(this_int, (int)other.localversion_segments[i]);
                                if (res != 0)
                                    return res;
                            }
                        }
                    }

                    res = cmp(localversion_segments.Length, other.localversion_segments.Length);
                }
            }

            return res;
        }

        public int Compare(string other)
        {
            return Compare(new VersionIdentifier(other));
        }

        public override bool Equals(object obj)
        {
            VersionIdentifier id = obj as VersionIdentifier;
            if (id != null)
            {
                return Compare(id) == 0;
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            if (!is_valid)
            {
                return raw_version_string.GetHashCode();
            }
            else if (release.Length > 1 && release[release.Length-1] == 0)
            {
                var trimmed_version = new VersionIdentifier(this.ToString());
                int i = release.Length - 1;
                while (i > 1 && release[i] == 0)
                {
                    i--;
                }
                trimmed_version.release = new int[i];
                for (int j = 0; j < i; j++)
                {
                    trimmed_version.release[j] = release[j];
                }
                return trimmed_version.ToString().GetHashCode();
            }
            return this.ToString().GetHashCode();
        }

        public bool IsPrefix(VersionIdentifier other)
        {
            if (epoch != other.epoch)
                return false;

            for (int i=0; i<release.Length; i++)
            {
                if ((other.release.Length > i ? other.release[i] : 0) != release[i])
                    return false;
            }

            return true;
        }

        public bool IsPrefix(string other)
        {
            return IsPrefix(new VersionIdentifier(other));
        }

        public override string ToString()
        {
            if (!is_valid)
                return raw_version_string;

            StringBuilder result = new StringBuilder();

            if (epoch != 0)
            {
                result.Append(epoch);
                result.Append("!");
            }

            result.Append(release[0]);

            for (int i = 1; i < release.Length; i++)
            {
                result.Append(".");
                result.Append(release[i]);
            }

            if (prerelease_type != PrereleaseType.Final)
            {
                switch(prerelease_type)
                {
                    case PrereleaseType.Alpha:
                        result.Append("a");
                        break;
                    case PrereleaseType.Beta:
                        result.Append("b");
                        break;
                    case PrereleaseType.ReleaseCandidate:
                        result.Append("rc");
                        break;
                }
                result.Append(prerelease_version);
            }

            if (is_postrelease)
            {
                result.Append(".post");
                result.Append(postrelease_version);
            }

            if (is_devrelease)
            {
                result.Append(".dev");
                result.Append(devrelease_version);
            }

            if (is_localversion)
            {
                result.Append("+");
                result.Append(localversion_segments[0]);
                for (int i = 1; i < localversion_segments.Length; i++)
                {
                    result.Append(".");
                    result.Append(localversion_segments[i]);
                }
            }

            return result.ToString();
        }
    }
}
