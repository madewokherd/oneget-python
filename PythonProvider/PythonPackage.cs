using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OneGet.Sdk;

namespace PythonProvider
{
    class PythonPackage
    {
        public string name;
        public string version;
        public string status;
        public string summary;
        public string source;
        public string search_key;
        public PythonInstall install;
        private string distinfo_path;

        public PythonPackage(string name)
        {
            this.name = name;
        }

        public void ReadMetadata(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line == "")
                        break;
                    if (line.StartsWith("        "))
                        // Line continuation
                        continue;
                    int delim_index = line.IndexOf(": ");
                    if (delim_index != -1)
                    {
                        string name = line.Substring(0, delim_index);
                        string value = line.Substring(delim_index + 2);
                        name = name.ToLowerInvariant();
                        if (name == "name")
                            this.name = value;
                        else if (name == "version")
                            this.version = value;
                        else if (name == "summary")
                            this.summary = value;
                    }
                }
            }
        }

        public void ReadMetadata(string filename)
        {
            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                ReadMetadata(stream);
            }
        }

        public static PythonPackage FromDistInfo(string path, PythonInstall install, Request request)
        {
            var result = new PythonPackage(null);
            result.status = Constants.PackageStatus.Installed;
            result.distinfo_path = path;
            result.install = install;
            try
            {
                result.ReadMetadata(Path.Combine(path, "METADATA"));
            }
            catch (Exception e)
            {
                request.Debug(string.Format("Unexpected Exception thrown in 'Python::FromDistInfo' -- {1}\\{2}\r\n{3}"), e.GetType().Name, e.Message, e.StackTrace);
            }
            if (result.name != null)
                return result;
            return null;
        }

        internal string fastpath
        {
            get
            {
                if (distinfo_path != null)
                    return string.Format("distinfo:{0}", distinfo_path);
                else if (source != null)
                    return string.Format("pypi:{0}#{1}/{2}", source, name, version);
                return null;
            }
        }

        internal void YieldSelf(Request request)
        {
            request.YieldSoftwareIdentity(fastpath, name, version ?? "unknown", "pep386", summary ?? "", source ?? "", search_key ?? "", "", "");
        }

        internal bool MatchesName(string name, Request request)
        {
            return this.name.Contains(name);
        }
    }
}
