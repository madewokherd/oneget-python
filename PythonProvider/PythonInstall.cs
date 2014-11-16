using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using OneGet.ProviderSDK;

namespace PythonProvider
{
    class PythonInstall
    {
        public string install_path;
        public string exe_path;
        public string python_version;

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

        private string GetPythonVersion()
        {
            return QueryPython("-c \"import sys;sys.stdout.write(str(sys.version_info[0])+'.'+str(sys.version_info[1])+'.'+str(sys.version_info[2]))\"");
        }

        public static PythonInstall FromPath(string installpath, Request request)
        {
            try
            {
                PythonInstall result = new PythonInstall();
                result.install_path = installpath;
                result.exe_path = Path.Combine(installpath, "python.exe");
                if (File.Exists(result.exe_path))
                {
                    result.python_version = result.GetPythonVersion();
                    if (!string.IsNullOrEmpty(result.python_version))
                        return result;
                }
            }
            catch
            {
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
            if (Environment.Is64BitOperatingSystem)
            {
                FindEnvironments(result, true, false, request);
                FindEnvironments(result, true, true, request);
            }
            FindEnvironments(result, false, false, request);
            FindEnvironments(result, false, true, request);
            return result;
        }

        public string GlobalSiteFolder()
        {
            return QueryPython("-c \"import sys;import distutils.sysconfig;sys.stdout.write(distutils.sysconfig.get_python_lib())\"");
        }
    }
}
