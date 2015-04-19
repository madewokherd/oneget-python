﻿using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OneGet.Sdk;

namespace PythonProvider
{
    class PythonInstall
    {
        public string install_path;
        public string exe_path;
        public VersionIdentifier python_version;
        private string global_site_folder;

        private PythonInstall()
        {
        }

        private string QueryPython(string arguments)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exe_path;
            startinfo.Arguments = arguments;
            startinfo.RedirectStandardOutput = true;
            startinfo.UseShellExecute = false;
            Process proc = Process.Start(startinfo);
            return proc.StandardOutput.ReadToEnd();
        }

        private VersionIdentifier GetPythonVersion(string version_str)
        {
            string[] version_parts = version_str.Split(new char[]{'.'}, 6);

            if (version_parts.Length != 5)
                return null;

            VersionIdentifier result = new VersionIdentifier("0.0.0");

            int part;

            if (!int.TryParse(version_parts[0], out part) || part < 0)
                return null;
            result.release[0] = part;
            if (!int.TryParse(version_parts[1], out part) || part < 0)
                return null;
            result.release[1] = part;
            if (!int.TryParse(version_parts[2], out part) || part < 0)
                return null;
            result.release[2] = part;

            switch (version_parts[3])
            {
                case "alpha":
                    result.prerelease_type = VersionIdentifier.PrereleaseType.Alpha;
                    break;
                case "beta":
                    result.prerelease_type = VersionIdentifier.PrereleaseType.Beta;
                    break;
                case "candidate":
                    result.prerelease_type = VersionIdentifier.PrereleaseType.ReleaseCandidate;
                    break;
                case "final":
                    result.prerelease_type = VersionIdentifier.PrereleaseType.Final;
                    break;
                default:
                    return null;
            }

            if (!int.TryParse(version_parts[4], out part) || part < 0)
                return null;
            result.prerelease_version = part;

            return result;
        }

        private static string FindPythonScript(string filename)
        {
            Uri assembly_url = new Uri(Assembly.GetExecutingAssembly().CodeBase);
            string result = Path.Combine(Path.GetDirectoryName(assembly_url.LocalPath), "python", filename);
            if (!File.Exists(result))
            {
                throw new FileNotFoundException("Included python script not found", result);
            }
            return result;
        }

        private void ReadInterpreterInfo()
        {
            string info = QueryPython(string.Format("\"{0}\"", FindPythonScript("get_info.py")));

            string[] parts = info.Split(new char[]{'\n'}, 3);
            if (parts.Length != 2)
                throw new Exception(string.Format("Bad output from python interpreter at {0}", exe_path));

            python_version = GetPythonVersion(parts[0]);
            global_site_folder = parts[1];
        }

        public static PythonInstall FromPath(string installpath, Request request)
        {
            try
            {
                PythonInstall result = new PythonInstall();
                if (Directory.Exists(installpath))
                {
                    result.install_path = installpath;
                    result.exe_path = Path.Combine(installpath, "python.exe");
                }
                else
                {
                    result.install_path = Path.GetDirectoryName(installpath);
                    result.exe_path = installpath;
                }
                result.ReadInterpreterInfo();
                string requested_version_str = request.GetOptionValue("PythonVersion");
                if (!string.IsNullOrEmpty(requested_version_str))
                {
                    VersionIdentifier requested_version = new VersionIdentifier(requested_version_str);
                    if (!requested_version.IsPrefix(result.python_version))
                        return null;
                }
                return result;
            }
            catch (Exception e)
            {
                request.Debug("Python at {0} isn't usable: {1}", installpath, e);
            }
            return null;
        }

        private static void FindEnvironments(List<PythonInstall> result, bool win64, bool user, Request request)
        {
            using (RegistryKey basekey = RegistryKey.OpenBaseKey(
                user ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                win64 ? RegistryView.Registry64 : RegistryView.Registry32))
            {
                RegistryKey pythoncore = basekey.OpenSubKey(@"Software\Python\PythonCore");
                if (pythoncore != null)
                {
                    foreach (string version in pythoncore.GetSubKeyNames())
                    {
                        RegistryKey installpathkey = pythoncore.OpenSubKey(string.Format(@"{0}\InstallPath", version));
                        if (installpathkey != null)
                        {
                            PythonInstall install = FromPath(installpathkey.GetValue(null).ToString(), request);
                            if (install != null)
                            {
                                result.Add(install);
                                request.Debug("Python::FindInstalledEnvironments found {0} in {1}", install.python_version, install.install_path);
                            }
                            installpathkey.Dispose();
                        }
                    }
                    pythoncore.Dispose();
                }
            }
        }

        public static List<PythonInstall> FindEnvironments(Request request)
        {
            List<PythonInstall> result = new List<PythonInstall>();
            string requested_location = request.GetOptionValue("PythonLocation");
            if (!string.IsNullOrEmpty(requested_location))
            {
                PythonInstall install = FromPath(requested_location, request);
                if (install != null)
                    result.Add(install);
            }
            else
            {
                if (Environment.Is64BitOperatingSystem)
                {
                    FindEnvironments(result, true, false, request);
                }
                FindEnvironments(result, false, false, request);
                FindEnvironments(result, false, true, request);
            }
            return result;
        }

        public string GlobalSiteFolder()
        {
            return global_site_folder;
        }

        // Compatibility tags - https://www.python.org/dev/peps/pep-0425/
        private IEnumerable<string> PythonTags()
        {
            yield return string.Format("py{0}", python_version.release[0]);
            yield return string.Format("py{0}{1}", python_version.release[0], python_version.release[1]);
        }

        public bool CompatibleWithPythonTag(string tag)
        {
            foreach (var my_tag in PythonTags())
                if (my_tag == tag)
                    return true;
            return false;
        }

        public bool CompatibleWithAbiTag(string tag)
        {
            return tag == "none";
        }

        public bool CompatibleWithPlatformTag(string tag)
        {
            return tag == "any";
        }

        public bool CompatibleWithTag(string tag)
        {
            //FIXME: distinguish between different interpreters, abi's, and platforms
            string[] tag_bits = tag.Split('-');

            bool python_ok = false;
            foreach (var python_tag in tag_bits[0].Split('.'))
                if (CompatibleWithPythonTag(python_tag))
                    python_ok = true;
            if (!python_ok)
                return false;

            bool abi_ok = false;
            foreach (var abi_tag in tag_bits[1].Split('.'))
                if (CompatibleWithAbiTag(abi_tag))
                    abi_ok = true;
            if (!abi_ok)
                return false;

            bool platform_ok = false;
            foreach (var platform_tag in tag_bits[2].Split('.'))
                if (CompatibleWithPlatformTag(platform_tag))
                    platform_ok = true;
            if (!platform_ok)
                return false;

            return true;
        }
    }
}
