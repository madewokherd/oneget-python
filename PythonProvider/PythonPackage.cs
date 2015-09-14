using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using OneGet.Sdk;
using Microsoft.PackageManagement.Archivers.Compression;
using Microsoft.PackageManagement.Archivers.Compression.Zip;

namespace PythonProvider
{
    struct PackageDownload
    {
        public string url;
        public string basename;
        public string md5_digest;
        public string packagetype;
        public long size;
    }

    struct DistRequirement
    {
        public string name;
        public bool has_version_specifier;
        public VersionSpecifier version_specifier;
        public string condition;
        public string raw_string;

        public static DistRequirement Parse(string requirement)
        {
            DistRequirement result = new DistRequirement();
            result.raw_string = requirement;
            if (requirement.Contains(';'))
            {
                string[] parts = requirement.Split(new char[] {';'}, 2);
                requirement = parts[0].Trim();
                result.condition = parts[1].Trim();
            }
            if (requirement.Contains('('))
            {
                string[] parts = requirement.TrimEnd(')').Split(new char[] {'('}, 2);
                result.name = parts[0].Trim();
                result.has_version_specifier = true;
                result.version_specifier = new VersionSpecifier(parts[1]);
            }
            else
            {
                result.name = requirement.Trim();
                result.has_version_specifier = false;
            }
            return result;
        }
    }

    class PythonPackage
    {
        public string name;
        public VersionIdentifier version;
        public string status;
        public string summary;
        public string source;
        public string sourceurl;
        public PackageDownload[] downloads;
        public string search_key;
        public PythonInstall install;
        private string distinfo_path;
        private string archive_path;

        public List<DistRequirement> requires_dist;

        public bool incomplete_metadata; // If true, we can only rely on name, version, source, and downloads

        //wheel metadata
        private string wheel_version;
        private List<string> tags;
        private bool is_wheel;

        public PythonPackage(string name)
        {
            this.name = name;
            this.requires_dist = new List<DistRequirement>();
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
                            this.version = new VersionIdentifier(value);
                        else if (name == "summary")
                            this.summary = value;
                        else if (name == "requires-dist")
                            this.requires_dist.Add(DistRequirement.Parse(value));
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

        private void ReadWheelMetadata(Stream stream)
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
                        if (name == "wheel-version")
                            this.wheel_version = value;
                        else if (name == "tag")
                        {
                            if (this.tags == null)
                                this.tags = new List<string>();
                            this.tags.Add(value);
                        }
                    }
                }
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

        private static string escape_package_name(string name)
        {
            List<string> alphanumeric_runs = new List<string>();
            int i = 0;
            while (true)
            {
                int run_start = i;
                while (i < name.Length && char.IsLetterOrDigit(name[i]))
                {
                    i++;
                }
                if (i == run_start)
                    alphanumeric_runs.Add("");
                else
                    alphanumeric_runs.Add(name.Substring(run_start, i - run_start));
                if (i == name.Length)
                    break;
                while (i < name.Length && !char.IsLetterOrDigit(name[i]))
                    i++;
            }
            return string.Join("_", alphanumeric_runs);
        }

        public static IEnumerable<PythonPackage> PackagesFromFile(string path, Request request)
        {
            ZipInfo zi=null;
            try
            {
                zi = new ZipInfo(path);
            }
            catch { }
            if (zi != null)
            {
                foreach (var subfile in zi.GetFiles())
                {
                    if (subfile.Path.EndsWith(".dist-info") && subfile.Name == "METADATA")
                    {
                        if (subfile.Path.Contains("/"))
                        {
                            // just so we know we can use these in fastpath
                            continue;
                        }
                        var result = new PythonPackage(null);
                        result.status = Constants.PackageStatus.Available;
                        result.archive_path = path;
                        using (var metadata_stream = subfile.OpenRead())
                        {
                            result.ReadMetadata(metadata_stream);
                        }
                        using (var wheel_metadata_stream = zi.GetFile(string.Format("{0}\\WHEEL", subfile.Path)).OpenRead())
                        {
                            result.ReadWheelMetadata(wheel_metadata_stream);
                        }
                        if (subfile.Path != string.Format("{0}-{1}.dist-info", escape_package_name(result.name), result.version.raw_version_string))
                            continue;
                        result.is_wheel = true;
                        yield return result;
                    }
                }
            }
        }

        internal virtual string fastpath
        {
            get
            {
                if (distinfo_path != null)
                    return string.Format("distinfo:{0}|{1}", install.exe_path, distinfo_path);
                else if (source != null)
                    return string.Format("pypi:{0}#{1}#{2}/{3}", source, sourceurl, name, version.raw_version_string);
                else if (archive_path != null)
                    return string.Format("archive:{0}/{1}/{2}", name, version.raw_version_string, archive_path);
                return null;
            }
        }

        public static PythonPackage FromFastReference(string fastreference, Request request)
        {
            if (fastreference.StartsWith("distinfo:"))
            {
                string[] parts = fastreference.Substring(9).Split(new char[] { '|' }, 2);
                PythonInstall install = PythonInstall.FromPath(parts[0], request);
                return FromDistInfo(parts[1], install, request);
            }
            else if (fastreference.StartsWith("pypi:"))
            {
                string[] parts = fastreference.Substring(5).Split(new char[] { '#' }, 3);
                string source = parts[0];
                string sourceurl = parts[1];
                parts = parts[2].Split(new char[] { '/' });
                string name = parts[0];
                string version = parts[1];
                return PyPI.GetPackage(new Tuple<string,string>(source, sourceurl), name, version, request);
            }
            else if (fastreference.StartsWith("archive:"))
            {
                string[] parts = fastreference.Substring(8).Split(new char[] { '/' }, 3);
                string name = parts[0];
                string version = parts[1];
                string archive_path = parts[2];
                foreach (var package in PackagesFromFile(archive_path, request))
                {
                    if (package.name == name && package.version.Compare(version) == 0)
                        return package;
                }
            }
            else if (fastreference.StartsWith("pythonweb:"))
            {
                return PythonWebsite.PackageFromWebResource(fastreference.Substring(10), request);
            }
            else if (fastreference.StartsWith("installedpython:"))
            {
                return PythonInstall.FromPath(fastreference.Substring(16), request);
            }
            return null;
        }

        internal void YieldSelf(Request request)
        {
            request.Debug("YIELDING: {0} {1}", name, version.ToString());
            request.YieldSoftwareIdentity(fastpath, name, version == null ? "unknown" : version.ToString(), "pep440", summary ?? "", source ?? archive_path ?? "", search_key ?? "", "", "");
        }

        internal bool MatchesName(string name, Request request)
        {
            return this.name.Contains(name);
        }

        private bool CanInstall(PythonInstall install, PackageDownload download, out bool install_specific, Request request)
        {
            install_specific = false;
            if (download.packagetype == "bdist_wheel")
            {
                string tag = download.basename;
                if (tag.EndsWith(".whl"))
                {
                    tag = tag.Substring(0, tag.Length - 4);
                }
                int platform_dash = tag.LastIndexOf('-');
                if (platform_dash <= 0) return false;
                int abi_dash = tag.LastIndexOf('-', platform_dash - 1);
                if (abi_dash <= 0) return false;
                int python_dash = tag.LastIndexOf('-', abi_dash - 1);
                if (python_dash <= 0) return false;
                tag = tag.Substring(python_dash + 1);

                install_specific = true;

                if (install.CompatibleWithTag(tag))
                    return true;

                return false;
            }
            return true;
        }

        private bool CanInstall(PythonInstall install, PackageDownload download, bool install_64bit, out bool install_specific, Request request)
        {
            install_specific = false;
            if (download.packagetype == "bdist_wheel")
            {
                string tag = download.basename;
                if (tag.EndsWith(".whl"))
                {
                    tag = tag.Substring(0, tag.Length - 4);
                }
                int platform_dash = tag.LastIndexOf('-');
                if (platform_dash <= 0) return false;
                int abi_dash = tag.LastIndexOf('-', platform_dash - 1);
                if (abi_dash <= 0) return false;
                int python_dash = tag.LastIndexOf('-', abi_dash - 1);
                if (python_dash <= 0) return false;
                tag = tag.Substring(python_dash + 1);

                install_specific = true;

                if (install.CompatibleWithTag(tag, install_64bit))
                    return true;

                return false;
            }
            return true;
        }

        public bool CanInstall(PythonInstall install, Request request)
        {
            if (is_wheel)
            {
                if (this.tags == null)
                    return true;
                foreach (var tag in this.tags)
                {
                    if (install.CompatibleWithTag(tag))
                        return true;
                }
                return false;
            }
            else if (source != null)
            {
                bool any_install_specific_download = false;

                foreach (var download in downloads)
                {
                    bool install_specific;
                    if (CanInstall(install, download, out install_specific, request))
                        return true;
                    if (install_specific)
                        any_install_specific_download = true;
                }
                return !any_install_specific_download;
            }
            return true;
        }

        public bool CanInstall(PythonInstall install, bool install_64bit, Request request)
        {
            if (is_wheel)
            {
                if (this.tags == null)
                    return true;
                foreach (var tag in this.tags)
                {
                    if (install.CompatibleWithTag(tag, install_64bit))
                        return true;
                }
                return false;
            }
            else if (source != null)
            {
                bool any_install_specific_download = false;

                foreach (var download in downloads)
                {
                    bool install_specific;
                    if (CanInstall(install, download, install_64bit, out install_specific, request))
                        return true;
                    if (install_specific)
                        any_install_specific_download = true;
                }
                return !any_install_specific_download;
            }
            return true;
        }

        private string hash_to_string(byte[] hash)
        {
            StringBuilder result = new StringBuilder(hash.Length * 2);

            foreach (byte b in hash)
            {
                result.Append(b.ToString("x2"));
            }
            return result.ToString();
        }

        // sigh
        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Win32CreateDirectory(string pathname, IntPtr securityattributes);
        
        private bool DoDownload(PackageDownload download, out string tempdir, out string filename, Request request)
        {
            bool created = false;
            tempdir = "";

            while (!created)
            {
                tempdir = Path.GetTempPath() + Guid.NewGuid().ToString();
                if (Win32CreateDirectory(tempdir, IntPtr.Zero))
                {
                    created = true;
                }
                else
                {
                    if (Marshal.GetLastWin32Error() != 183) // ERROR_ALREADY_EXISTS
                    {
                        throw new Win32Exception();
                    }
                }
            }
            filename = Path.Combine(tempdir, download.basename);
            request.ProviderServices.DownloadFile(new Uri(download.url), filename, request);

            if (!string.IsNullOrWhiteSpace(download.md5_digest))
            {
                request.Debug("expected md5sum: {0}", download.md5_digest);

                using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read))
                {
                    using (MD5 md5 = MD5.Create())
                    {
                        byte[] actual_hash = md5.ComputeHash(fs);
                        string actual_hash_string = hash_to_string(actual_hash);
                        request.Debug("actual md5sum: {0}", actual_hash_string);
                        if (actual_hash_string != download.md5_digest)
                        {
                            request.Error(ErrorCategory.MetadataError, name, "Downloaded file has incorrect MD5 sum");
                            File.Delete(filename);
                            Directory.Delete(tempdir);
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public bool SatisfiesDependency(PythonInstall install, DistRequirement dep, Request request)
        {
            if (dep.condition != null)
                // FIXME: handle this, somehow?
                return true;
            if (name != dep.name)
                return false;
            if (dep.has_version_specifier)
            {
                if (!dep.version_specifier.MatchesVersion(version))
                    return false;
            }
            return true;
        }

        private Dictionary<string, PythonPackage> SimpleResolveDependencies(PythonInstall install, out DistRequirement failed_dependency, Request request)
        {
            Dictionary<string, PythonPackage> result = new Dictionary<string, PythonPackage>();
            Queue<DistRequirement> to_resolve = new Queue<DistRequirement>();
            var installed_packages = new Dictionary<string, PythonPackage>();
            bool need_recheck=true; // True if we're [up|down]grading a package, and therefore may need to recheck deps

            foreach (var package in install.FindInstalledPackages(null, null, request))
            {
                installed_packages[package.name] = package;
            }

            while (need_recheck)
            {
                need_recheck = false;

                to_resolve.Clear();
                foreach (var dep in requires_dist)
                {
                    request.Debug("Adding dependency {0}", dep.raw_string);
                    to_resolve.Enqueue(dep);
                }

                result.Clear();

                while (to_resolve.Count != 0)
                {
                    var dep = to_resolve.Dequeue();
                    PythonPackage package;

                    request.Debug("Examining dependency {0}", dep.raw_string);

                    if (result.TryGetValue(dep.name, out package))
                    {
                        if (!package.SatisfiesDependency(install, dep, request))
                        {
                            failed_dependency = dep;
                            return null;
                        }
                        request.Debug("Satisfied by package to install {0} {1}", package.name, package.version.ToString());
                    }
                    else
                    {
                        if (installed_packages.TryGetValue(dep.name, out package))
                        {
                            if (package.SatisfiesDependency(install, dep, request))
                            {
                                request.Debug("Satisfied by installed package {0} {1}", package.name, package.version.ToString());
                                continue;
                            }
                            else
                            {
                                request.Debug("Not satisfied by installed package {0} {1}", package.name, package.version.ToString());
                                need_recheck = true;
                            }
                        }

                        // find newest version of package that satisfies dependency

                        package = null;
                        foreach (var candidate_package in PyPI.ExactSearch(dep.name, request))
                        {
                            request.Debug("Examining {0} {1}", candidate_package.name, candidate_package.version.ToString());
                            if (candidate_package.SatisfiesDependency(install, dep, request))
                            {
                                package = candidate_package;
                                break;
                            }
                        }

                        if (package == null)
                        {
                            request.Debug("Cannot satisfy dependency");
                            failed_dependency = dep;
                            return null;
                        }

                        request.Debug("Selecting {0} {1}", package.name, package.version.ToString());

                        // need to do another request to find dependencies
                        if (package.incomplete_metadata)
                        {
                            package = PyPI.GetPackage(new Tuple<string, string>(package.source, package.sourceurl),
                                package.name, package.version.raw_version_string, request);
                        }

                        // add its dependencies to queue
                        foreach (var dep2 in package.requires_dist)
                        {
                            request.Debug("Adding dependency {0}", dep2.raw_string);
                            to_resolve.Enqueue(dep2);
                        }

                        result[package.name] = package;
                    }
                }
            }

            failed_dependency = default(DistRequirement);
            return result;
        }

        public bool CheckDependencies(PythonInstall install, out DistRequirement failed_dependency, Request request)
        {
            failed_dependency = new DistRequirement();
            if (requires_dist.Count == 0)
                return true;
            List<PythonPackage> installed_packages = new List<PythonPackage>(install.FindInstalledPackages(null, null, request));
            foreach (var dep in requires_dist)
            {
                if (dep.condition != null)
                    // FIXME: handle this, somehow?
                    continue;
                bool satisfied_dependency = false;
                foreach (var package in installed_packages)
                {
                    if (package.SatisfiesDependency(install, dep, request))
                    {
                        satisfied_dependency = true;
                        break;
                    }
                }
                if (!satisfied_dependency)
                {
                    failed_dependency = dep;
                    return false;
                }
            }
            return true;
        }

        private bool Install(PythonInstall install, PackageDownload download, Request request)
        {
            if (download.packagetype == "bdist_wheel")
            {
                string tempdir, filename;
                if (!DoDownload(download, out tempdir, out filename, request))
                    return false;

                try
                {
                    foreach (var package in PackagesFromFile(filename, request))
                    {
                        if (package.name == name && package.version.raw_version_string == version.raw_version_string)
                            return package.Install(install, request);
                    }
                    request.Error(ErrorCategory.MetadataError, name, "Downloaded package file doesn't contain the expected package.");
                    return false;
                }
                finally
                {
                    File.Delete(filename);
                    Directory.Delete(tempdir);
                }
            }
            request.Error(ErrorCategory.NotImplemented, name, "installing not implemented for package type {0}", download.packagetype);
            return false;
        }

        private static bool InstallDependencies(PythonInstall install, Dictionary<string, PythonPackage> deps, Request request)
        {
            while (deps.Count != 0)
            {
                var enumerator = deps.GetEnumerator();
                enumerator.MoveNext();
                PythonPackage package = enumerator.Current.Value;

                bool unsatisfied_deps=true;
                while (unsatisfied_deps)
                {
                    unsatisfied_deps = false;
                    foreach (var dep in package.requires_dist)
                    {
                        if (deps.ContainsKey(dep.name))
                        {
                            // FIXME: Infinite loop if dep graph has cycles
                            package = deps[dep.name];
                            unsatisfied_deps = true;
                            break;
                        }
                    }
                }

                if (!package.Install(install, request))
                    return false;

                deps.Remove(package.name);
            }

            return true;
        }

        public bool Install(PythonInstall install, Request request)
        {
            DistRequirement failed_dependency;
            request.Debug("Installing {0} {1}", name, version.ToString());
            if (incomplete_metadata)
            {
                return PyPI.GetPackage(new Tuple<string, string>(source, sourceurl), name, version.raw_version_string, request).Install(install, request);
            }
            if (!CheckDependencies(install, out failed_dependency, request))
            {
                var deps = SimpleResolveDependencies(install, out failed_dependency, request);
                if (deps == null)
                {
                    request.Error(ErrorCategory.NotInstalled, name, string.Format("Dependency '{0}' not found, unable to resolve automatically.", failed_dependency.raw_string));
                    return false;
                }

                if (!InstallDependencies(install, deps, request))
                    return false;
            }
            if (is_wheel)
            {
                if (install.InstallWheel(archive_path, request) != 0)
                {
                    request.Error(ErrorCategory.NotSpecified, name, "wheel install failed");
                    return false;
                }
                foreach (var package in install.FindInstalledPackages(name, null, request))
                {
                    if (package.version.raw_version_string != version.raw_version_string)
                        package.Uninstall(request);
                }
                return true;
            }
            else if (source != null)
            {
                foreach (var download in downloads)
                {
                    bool install_specific;
                    if (CanInstall(install, download, out install_specific, request) && install_specific)
                    {
                        return Install(install, download, request);
                    }
                }
                request.Error(ErrorCategory.NotImplemented, name, "installing not implemented for this package type");
                return false;
            }
            else
            {
                request.Error(ErrorCategory.NotImplemented, name, "installing not implemented for this package type");
                return false;
            }
        }

        public virtual bool Uninstall(Request request)
        {
            if (distinfo_path != null)
            {
                return install.UninstallDistinfo(distinfo_path, request) == 0;
            }
            else
            {
                request.Error(ErrorCategory.NotImplemented, name, "uninstalling not implemented for this package type");
                return false;
            }
        }
    }
}
