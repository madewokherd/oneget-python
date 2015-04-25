﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OneGet.Sdk;
using Microsoft.PackageManagement.Archivers.Compression;
using Microsoft.PackageManagement.Archivers.Compression.Zip;

namespace PythonProvider
{
    class PythonPackage
    {
        public string name;
        public string version;
        public string status;
        public string summary;
        public string source;
        public string sourceurl;
        public string search_key;
        public PythonInstall install;
        private string distinfo_path;
        private string archive_path;

        //wheel metadata
        private string wheel_version;
        private bool root_is_purelib;
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
                        else if (name == "root-is-purelib" && value == "true")
                            this.root_is_purelib = true;
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

        public bool CanInstall(PythonInstall install, Request request)
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

        public bool Install(PythonInstall install, Request request)
        {
            if (is_wheel)
            {
                if (install.InstallWheel(archive_path) != 0)
                {
                    request.Error(ErrorCategory.NotSpecified, name, "wheel install failed");
                    return false;
                }
                return true;
            }
            else
            {
                request.Error(ErrorCategory.NotImplemented, name, "installing not implemented for this package type");
                return false;
            }
        }
    }
}
