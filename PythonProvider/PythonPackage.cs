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

    class PythonPackage
    {
        public string name;
        public string version;
        public string status;
        public string summary;
        public string source;
        public string sourceurl;
        public PackageDownload[] downloads;
        public string search_key;
        public PythonInstall install;
        private string distinfo_path;
        private string archive_path;

        //wheel metadata
        private string wheel_version;
        private List<string> tags;
        private bool is_wheel;

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
                        if (subfile.Path != string.Format("{0}-{1}.dist-info", result.name, result.version))
                            continue;
                        result.is_wheel = true;
                        yield return result;
                    }
                }
            }
        }

        internal string fastpath
        {
            get
            {
                if (distinfo_path != null)
                    return string.Format("distinfo:{0}", distinfo_path);
                else if (source != null)
                    return string.Format("pypi:{0}#{1}#{2}/{3}", source, sourceurl, name, version);
                else if (archive_path != null)
                    return string.Format("archive:{0}/{1}/{2}", name, version, archive_path);
                return null;
            }
        }

        public static PythonPackage FromFastReference(string fastreference, Request request)
        {
            if (fastreference.StartsWith("distinfo:"))
            {
                throw new NotImplementedException("can't read distinfo: fast references yet");
                // Need to figure out how to identify the python install that owns this package
            }
            else if (fastreference.StartsWith("pypi:"))
            {
                string[] parts = fastreference.Substring(5).Split(new char[] { '#' }, 3);
                string source = parts[0];
                string sourceurl = parts[1];
                parts = parts[2].Split(new char[] { '/' });
                string name = parts[0];
                string version = parts[1];
                return PyPI.GetPackage(new Tuple<string,string>(source, sourceurl), name, version);
            }
            else if (fastreference.StartsWith("archive:"))
            {
                string[] parts = fastreference.Substring(8).Split(new char[] { '/' }, 3);
                string name = parts[0];
                string version = parts[1];
                string archive_path = parts[2];
                foreach (var package in PackagesFromFile(archive_path, request))
                {
                    if (package.name == name && package.version == version)
                        return package;
                }
            }
            return null;
        }

        internal void YieldSelf(Request request)
        {
            request.YieldSoftwareIdentity(fastpath, name, version ?? "unknown", "pep440", summary ?? "", source ?? archive_path ?? "", search_key ?? "", "", "");
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
                        if (package.name == name && package.version == version)
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
            request.Error(ErrorCategory.NotImplemented, name, "installing not implemented for this package type");
            return false;
        }

        public bool Install(PythonInstall install, Request request)
        {
            if (install.NeedAdminToWrite())
            {
                request.Error(ErrorCategory.PermissionDenied, name, "You need to be admin to modify this Python install.");
                return false;
            }
            if (is_wheel)
            {
                if (install.InstallWheel(archive_path) != 0)
                {
                    request.Error(ErrorCategory.NotSpecified, name, "wheel install failed");
                    return false;
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
    }
}
