using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using OneGet.Sdk;
using Newtonsoft.Json.Linq;

namespace PythonProvider
{
    class PythonInstall : PythonPackage
    {
        public string install_path;
        public string exe_path;
        private string global_site_folder;
        private string[] supported_tags;
        public string web_resource;
        private bool from_registry;
        private bool reg_user;
        public bool is_64bit;

        internal PythonInstall() : base("Python")
        {
        }

        private string QueryPython(string arguments, Request request)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exe_path;
            startinfo.Arguments = arguments;
            startinfo.RedirectStandardOutput = true;
            startinfo.UseShellExecute = false;
            request.Debug("Running command: {0} {1}", startinfo.FileName, startinfo.Arguments);
            Process proc = Process.Start(startinfo);
            return proc.StandardOutput.ReadToEnd();
        }

        public int InstallWheel(string filename, Request request)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exe_path;
            startinfo.Arguments = string.Format("\"{0}\" \"{1}\"", FindPythonScript("install_wheel.py"), filename);
            if (NeedAdminToWrite())
            {
                startinfo.UseShellExecute = true;
                startinfo.Verb = "runas";
            }
            else
            {
                startinfo.UseShellExecute = false;
            }
            request.Debug("Running command{0}: {1} {2}", startinfo.UseShellExecute ? " as admin" : "", startinfo.FileName, startinfo.Arguments);
            Process proc = Process.Start(startinfo);
            proc.WaitForExit();
            return proc.ExitCode;
        }

        public int UninstallDistinfo(string path, Request request)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exe_path;
            startinfo.Arguments = string.Format("\"{0}\" \"{1}\"", FindPythonScript("uninstall_distinfo.py"), path);
            if (NeedAdminToWrite())
            {
                startinfo.UseShellExecute = true;
                startinfo.Verb = "runas";
            }
            else
            {
                startinfo.UseShellExecute = false;
            }
            request.Debug("Running command{0}: {1} {2}", startinfo.UseShellExecute ? " as admin" : "", startinfo.FileName, startinfo.Arguments);
            Process proc = Process.Start(startinfo);
            proc.WaitForExit();
            return proc.ExitCode;
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

        private void ReadInterpreterInfo(Request request)
        {
            string info = QueryPython(string.Format("\"{0}\"", FindPythonScript("get_info.py")), request);

            string[] parts = info.Split(new char[]{'\0'}, 5);
            if (parts.Length != 4)
                throw new Exception(string.Format("Bad output from python interpreter at {0}", exe_path));

            version = GetPythonVersion(parts[0]);
            global_site_folder = parts[1];
            is_64bit = (parts[2] == "8");
            supported_tags = parts[3].Split('.');
        }

        private string[] GetSupportedTags(bool install_64bit)
        {
            if (supported_tags != null)
                return supported_tags;

            List<string> result = new List<string>();
            string majorversion = version.release[0].ToString();
            int minorversion = version.release[1];
            string platform;
            if (install_64bit)
                platform = "win_amd64";
            else
                platform = "win32";
            result.Add(string.Format("cp{0}{1}-none-{2}", majorversion, minorversion, platform));
            result.Add(string.Format("cp{0}{1}-none-any", majorversion, minorversion));
            result.Add(string.Format("cp{0}-none-any", majorversion));
            for (int i=minorversion-1; i>=0; i--)
                result.Add(string.Format("cp{0}{1}-none-any", majorversion, i));
            result.Add(string.Format("py{0}-none-{1}", majorversion, platform));
            result.Add(string.Format("py{0}{1}-none-any", majorversion, minorversion));
            result.Add(string.Format("py{0}-none-any", majorversion));
            for (int i = minorversion - 1; i >= 0; i--)
                result.Add(string.Format("py{0}{1}-none-any", majorversion, i));
            supported_tags = result.ToArray();
            return supported_tags;
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
                result.ReadInterpreterInfo(request);
                string requested_version_str = request.GetOptionValue("PythonVersion");
                if (!string.IsNullOrEmpty(requested_version_str))
                {
                    VersionIdentifier requested_version = new VersionIdentifier(requested_version_str);
                    if (!requested_version.IsPrefix(result.version))
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

        private static void FindEnvironments(List<PythonInstall> result, bool win64, bool user, HashSet<string> seen_paths, Request request)
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
                            string path = installpathkey.GetValue(null).ToString();
                            if (!seen_paths.Add(path))
                                continue;
                            PythonInstall install = FromPath(path, request);
                            if (install != null)
                            {
                                install.from_registry = true;
                                install.reg_user = user;
                                result.Add(install);
                                request.Debug("Python::FindInstalledEnvironments found {0} in {1}", install.version, install.install_path);
                            }
                            installpathkey.Dispose();
                        }
                    }
                    pythoncore.Dispose();
                }
            }
        }

        private static List<PythonInstall> FindEnvironments(bool force_registry, Request request)
        {
            List<PythonInstall> result = new List<PythonInstall>();
            string requested_location = request.GetOptionValue("PythonLocation");
            if (!force_registry && !string.IsNullOrEmpty(requested_location))
            {
                PythonInstall install = FromPath(requested_location, request);
                if (install != null)
                    result.Add(install);
            }
            else
            {
                HashSet<string> seen_paths = new HashSet<string>();
                if (Environment.Is64BitOperatingSystem)
                {
                    FindEnvironments(result, true, false, seen_paths, request);
                }
                FindEnvironments(result, false, false, seen_paths, request);
                FindEnvironments(result, false, true, seen_paths, request);
            }
            return result;
        }

        public static List<PythonInstall> FindEnvironments(Request request)
        {
            return FindEnvironments(false, request);
        }

        internal override string fastpath
        {
            get
            {
                if (exe_path != null)
                {
                    return string.Format("installedpython:{0}", exe_path);
                }
                else if (web_resource != null)
                {
                    return string.Format("pythonweb:{0}", web_resource);
                }
                return null;
            }
        }

        public IEnumerable<PythonPackage> FindInstalledPackages(string name, string required_version, Request request)
        {
            /* FIXME: optimize if name and required_version are specified. */
            string path = global_site_folder;
            string name_wc = string.IsNullOrWhiteSpace(name) ? "*" : name;
            string version_wc = string.IsNullOrWhiteSpace(required_version) ? "*" : required_version;
            request.Debug("Python::FindInstalledPackages searching {0}", path);
            foreach (string dir in Directory.EnumerateDirectories(path, string.Format("{0}-{1}.dist-info", name_wc, version_wc)))
            {
                request.Debug("Python::FindInstalledPackages trying {0}", dir);
                PythonPackage result = PythonPackage.FromDistInfo(dir, this, request);
                if (result != null)
                {
                    if (name != null && !result.MatchesName(name, request))
                        continue;
                    yield return result;
                }
            }
        }

        public string GlobalSiteFolder()
        {
            return global_site_folder;
        }

        public bool CompatibleWithTag(string tag)
        {
            string[] tag_bits = tag.Split('-');

            foreach (var python_tag in tag_bits[0].Split('.'))
                foreach (var abi_tag in tag_bits[1].Split('.'))
                    foreach (var platform_tag in tag_bits[2].Split('.'))
                    {
                        string specific_tag = string.Format("{0}-{1}-{2}", python_tag, abi_tag, platform_tag);
                        foreach (var supported_tag in supported_tags)
                            if (supported_tag == specific_tag)
                                return true;
                    }

            return false;
        }

        public bool CompatibleWithTag(string tag, bool install_64bit)
        {
            string[] tag_bits = tag.Split('-');

            string[] supported_tags = GetSupportedTags(install_64bit);

            foreach (var python_tag in tag_bits[0].Split('.'))
                foreach (var abi_tag in tag_bits[1].Split('.'))
                    foreach (var platform_tag in tag_bits[2].Split('.'))
                    {
                        string specific_tag = string.Format("{0}-{1}-{2}", python_tag, abi_tag, platform_tag);
                        foreach (var supported_tag in supported_tags)
                            if (supported_tag == specific_tag)
                                return true;
                    }

            return false;
        }

        public bool NeedAdminToWrite()
        {
            WindowsIdentity current_identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal current_principal = new WindowsPrincipal(current_identity);
            if (current_principal.IsInRole(WindowsBuiltInRole.Administrator))
                return false;

            var access_rules = Directory.GetAccessControl(global_site_folder).GetAccessRules(true, true, typeof(SecurityIdentifier));

            bool admin_has_access = false;
            bool user_has_access = false;

            foreach (FileSystemAccessRule rule in access_rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    /* Elevation isn't going to do anything about deny rules, so whatever. */
                    continue;
                if ((rule.FileSystemRights & FileSystemRights.CreateDirectories) == 0)
                    continue;

                if (rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)))
                    admin_has_access = true;
                else if (current_identity.User.Equals(rule.IdentityReference) ||
                         (rule.IdentityReference is SecurityIdentifier &&
                          current_principal.IsInRole((SecurityIdentifier)rule.IdentityReference)))
                    user_has_access = true;
            }
            return admin_has_access && !user_has_access;
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

        private bool DoDownload(JObject download, string extension, out string filename, Request request)
        {
            bool created = false;

            do
            {
                filename = Path.GetTempPath() + Guid.NewGuid().ToString() + extension;
                try
                {
                    File.Open(filename, FileMode.CreateNew).Close();
                    created = true;
                }
                catch (IOException)
                {
                }
            } while (!created);
            request.ProviderServices.DownloadFile(new Uri(download["url"].ToString()), filename, request);

            string md5sum = download["md5_sum"].ToString();
            request.Debug("expected md5sum: {0}", md5sum);

            using (FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read))
            {
                using (MD5 md5 = MD5.Create())
                {
                    byte[] actual_hash = md5.ComputeHash(fs);
                    string actual_hash_string = hash_to_string(actual_hash);
                    request.Debug("actual md5sum: {0}", actual_hash_string);
                    if (actual_hash_string != md5sum)
                    {
                        request.Error(ErrorCategory.MetadataError, name, "Downloaded file has incorrect MD5 sum");
                        File.Delete(filename);
                        return false;
                    }
                }
            }

            // FIXME: Verify gpg signature?

            return true;
        }

        private JObject FindDownload(bool install_64bit, out bool is_msi, Request request)
        {
            if (web_resource == null)
            {
                throw new InvalidOperationException("Installing an existing install of Python doesn't make sense");
            }

            JObject download = null;
            is_msi = false;
            foreach (JObject candidate in PythonWebsite.DownloadsFromWebResource(web_resource, request))
            {
                string name = candidate["name"].ToString();
                if (install_64bit)
                {
                    if (name == "Windows x86-64 MSI installer")
                    {
                        download = candidate;
                        is_msi = true;
                        break;
                    }
                    else if (name == "Windows x86-64 executable installer")
                    {
                        download = candidate;
                        is_msi = false;
                        break;
                    }
                }
                else
                {
                    if (name == "Windows x86 MSI installer")
                    {
                        download = candidate;
                        is_msi = true;
                        break;
                    }
                    else if (name == "Windows x86 executable installer")
                    {
                        download = candidate;
                        is_msi = false;
                        break;
                    }
                }
            }
            return download;
        }

        public bool CanInstall(bool install_64bit, Request request)
        {
            bool dummy;
            return (FindDownload(install_64bit, out dummy, request) != null);
        }

        public bool Install(bool install_64bit, Request request)
        {
            //bool user_scope = false;

            bool is_msi;

            JObject download = FindDownload(install_64bit, out is_msi, request);

            if (download == null)
            {
                request.Error(ErrorCategory.ResourceUnavailable, "Python", "Cannot find installer download for Python {0}", version.ToString());
                return false;
            }

            string filename;
            if (!DoDownload(download, is_msi ? ".msi" : ".exe", out filename, request))
            {
                return false;
            }

            bool success;
            if (is_msi)
            {
                success = request.ProviderServices.Install(filename, "", request);
            }
            else
            {
                // wix bootstrapper
                ProcessStartInfo startinfo = new ProcessStartInfo();
                startinfo.FileName = filename;
                startinfo.Arguments = "/quiet /install";
                startinfo.UseShellExecute = false;
                Process proc = Process.Start(startinfo);
                proc.WaitForExit();
                success = proc.ExitCode == 0;
            }
            File.Delete(filename);
            return success;
        }

        public bool Install(Request request)
        {
            bool install_64bit = Environment.Is64BitOperatingSystem;
            return Install(install_64bit, request);
        }

        private bool MaybeUninstallArp(RegistryKey key, string key_name, string arp_name, string alt_name, bool user, out bool succeeded, Request request)
        {
            succeeded = false;
            if ((key.GetValue("DisplayName") as string) != arp_name &&
                (key.GetValue("DisplayName") as string) != alt_name)
                return false;
            string cmd = key.GetValue("QuietUninstallString") as string;
            if (cmd == null)
                cmd = key.GetValue("UninstallString") as string;
            if (cmd == null)
                return false;
            ProcessStartInfo startinfo = new ProcessStartInfo();
            if ((key.GetValue("WindowsInstaller", 0, RegistryValueOptions.DoNotExpandEnvironmentNames) as int?) != 0)
            {
                startinfo.FileName = "msiexec.exe";
                startinfo.Arguments = string.Format("/passive /uninstall \"{0}\"", key_name);
            }
            else
            {
                startinfo.FileName = "cmd.exe";
                startinfo.Arguments = string.Format("/s /c \"{0}\"", cmd);
            }
            if (user)
            {
                startinfo.UseShellExecute = false;
            }
            else
            {
                startinfo.UseShellExecute = true;
                startinfo.Verb = "runas";
            }
            Process proc = Process.Start(startinfo);
            proc.WaitForExit();
            succeeded = (proc.ExitCode == 0);
            return true;
        }

        public override bool Uninstall(Request request)
        {
            if (exe_path == null)
            {
                return false;
            }

            if (!from_registry)
            {
                foreach (var package in FindEnvironments(true, request))
                {
                    if (package.exe_path == exe_path)
                        return package.Uninstall(request);
                }
                request.Error(ErrorCategory.ObjectNotFound, name, "couldn't find python install in registry");
                return false;
            }

            string arp_name = string.Format("Python {0} ({1}-bit)", version, is_64bit ? "64" : "32");
            string alt_name = string.Format("Python {0}{1}", version, is_64bit ? " (64-bit)" : "");
            bool succeeded;

            using (RegistryKey basekey = RegistryKey.OpenBaseKey(
                reg_user ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                is_64bit ? RegistryView.Registry64 : RegistryView.Registry32))
            {
                RegistryKey uninstallkey = basekey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallkey != null)
                {
                    using (uninstallkey)
                    {
                        foreach (string key_name in uninstallkey.GetSubKeyNames())
                        {
                            using (var key = uninstallkey.OpenSubKey(key_name, false))
                            {
                                if (key != null && MaybeUninstallArp(key, key_name, arp_name, alt_name, reg_user, out succeeded, request))
                                    return succeeded;
                            }
                        }
                    }
                }
            }

            if (reg_user)
            {
                /* MSI's "user" install can end up in HKLM because I don't know */

                using (RegistryKey basekey = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    is_64bit ? RegistryView.Registry64 : RegistryView.Registry32))
                {
                    RegistryKey uninstallkey = basekey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallkey != null)
                    {
                        using (uninstallkey)
                        {
                            foreach (string key_name in uninstallkey.GetSubKeyNames())
                            {
                                using (var key = uninstallkey.OpenSubKey(key_name, false))
                                {
                                    if (key != null && MaybeUninstallArp(key, key_name, arp_name, alt_name, false, out succeeded, request))
                                        return succeeded;
                                }
                            }
                        }
                    }
                }
            }

            request.Error(ErrorCategory.ObjectNotFound, this.name, "couldn't find ARP entry for python install");
            return false;
        }
    }
}
