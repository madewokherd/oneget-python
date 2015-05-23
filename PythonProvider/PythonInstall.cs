using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using OneGet.Sdk;

namespace PythonProvider
{
    class PythonInstall
    {
        public string install_path;
        public string exe_path;
        public VersionIdentifier python_version;
        private string global_site_folder;
        private string[] supported_tags;

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

        public int InstallWheel(string filename)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exe_path;
            startinfo.Arguments = string.Format("\"{0}\" \"{1}\"", FindPythonScript("install_wheel.py"), filename);
            startinfo.UseShellExecute = false;
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

        private void ReadInterpreterInfo()
        {
            string info = QueryPython(string.Format("\"{0}\"", FindPythonScript("get_info.py")));

            string[] parts = info.Split(new char[]{'\0'}, 4);
            if (parts.Length != 3)
                throw new Exception(string.Format("Bad output from python interpreter at {0}", exe_path));

            python_version = GetPythonVersion(parts[0]);
            global_site_folder = parts[1];
            supported_tags = parts[2].Split('.');
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

        public IEnumerable<PythonPackage> FindInstalledPackages(string name, VersionIdentifier required_version, Request request)
        {
            /* FIXME: optimize if name and required_version are specified. */
            string path = global_site_folder;
            request.Debug("Python::FindInstalledPackages searching {0}", path);
            foreach (string dir in Directory.EnumerateDirectories(path, "*.dist-info"))
            {
                request.Debug("Python::FindInstalledPackages trying {0}", dir);
                PythonPackage result = PythonPackage.FromDistInfo(dir, this, request);
                if (result != null)
                {
                    if (name != null && !result.MatchesName(name, request))
                        continue;
                    if (required_version != null && required_version.Compare(result.version) != 0)
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
    }
}
