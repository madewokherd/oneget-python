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

        public string invalid_string;

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
        public string localversion_label;

        public VersionIdentifier(string version_string)
        {
            if (!ParseVersion(version_string))
            {
                invalid_string = version_string;
            }
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

        private bool ParseVersion(string version_string)
        {
            int pos = 0;

            version_string = version_string.Trim().ToLowerInvariant();

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

            // up to 1 dev-release segment
            if (pos + 3 < version_string.Length &&
                version_string.Substring(pos, 4) == ".dev")
            {
                pos += 4;

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
                localversion_label = version_string.Substring(pos);

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

                pos = version_string.Length;
            }

            if (pos != version_string.Length)
                return false;

            return true;
        }

        public override string ToString()
        {
            if (invalid_string != null)
                return invalid_string;

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
                result.Append(localversion_label);
            }

            return result.ToString();
        }
    }
}
