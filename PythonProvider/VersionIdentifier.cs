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

        public VersionIdentifier(string version_string)
        {
            if (!ParseVersion(version_string))
            {
                invalid_string = version_string;
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

            return result.ToString();
        }
    }
}
