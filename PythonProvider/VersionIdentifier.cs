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
            if (pos < version_string.Length && char.IsLetter(version_string[pos]))
            {
                int prev_pos = pos;

                while (pos < version_string.Length && char.IsLetter(version_string[pos]))
                {
                    pos++;
                }

                string prerelease_identifier = version_string.Substring(prev_pos, pos - prev_pos);
                prerelease_type = GetPrereleaseType(prerelease_identifier);
                if (prerelease_type == PrereleaseType.Final)
                {
                    // unknown prerelease identifier
                    pos = prev_pos;
                }
                else
                {
                    prerelease_version = 0;
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

            if (pos != version_string.Length)
                return false;

            return true;
        }

        public override string ToString()
        {
            if (invalid_string != null)
                return invalid_string;

            StringBuilder result = new StringBuilder();

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

            return result.ToString();
        }
    }
}
