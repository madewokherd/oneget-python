using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using OneGet.ProviderSDK;

namespace PythonProvider
{
    public class PythonProvider
    {
        const string ProviderName = "Python";
        
        public void InitializeProvider(object requestObject)
        {
            try
            {
                using (var request = requestObject.As<Request>())
                {
                    request.Debug("Calling '{0}::InitializeProvider'", ProviderName);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(string.Format("Unexpected Exception thrown in '{0}::InitializeProvider' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
            }
        }

        public string GetPackageProviderName()
        {
            return ProviderName;
        }

        private string GetPythonVersion(string exepath)
        {
            ProcessStartInfo startinfo = new ProcessStartInfo();
            startinfo.FileName = exepath;
            startinfo.Arguments = "-c \"import sys;sys.stdout.write(str(sys.version_info[0])+'.'+str(sys.version_info[1]))\"";
            startinfo.RedirectStandardOutput = true;
            startinfo.UseShellExecute = false;
            Process proc = Process.Start(startinfo);
            return proc.StandardOutput.ReadToEnd();
        }

        private void FindInstalledEnvironments(List<Tuple<string,string>> result, bool win64, bool user, Request request)
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
                            try
                            {
                                string installpath = installpathkey.GetValue(null).ToString();
                                string exepath = Path.Combine(installpath, "python.exe");
                                if (File.Exists(exepath))
                                {
                                    string pythonversion = GetPythonVersion(exepath);
                                    result.Add(new Tuple<string, string>(pythonversion, installpath));
                                    request.Debug("Python::FindInstalledEnvironments found {0} in {1}", pythonversion, installpath);
                                }
                            }
                            catch (Exception e)
                            {
                                request.Debug(string.Format("Unexpected Exception thrown in '{0}::FindInstalledEnvironments' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
                            }
                            installpathkey.Dispose();
                        }
                    }
                    pythoncore.Dispose();
                }
            }
        }

        private List<Tuple<string, string>> FindInstalledEnvironments(Request request)
        {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();
            if (Environment.Is64BitOperatingSystem)
            {
                FindInstalledEnvironments(result, true, false, request);
                FindInstalledEnvironments(result, true, true, request);
            }
            FindInstalledEnvironments(result, false, false, request);
            FindInstalledEnvironments(result, false, true, request);
            return result;
        }

        public void GetInstalledPackages(string name, object requestObject)
        {
            try
            {
                using (var request = requestObject.As<Request>())
                {
                    request.Debug("Calling '{0}::GetInstalledPackages'", ProviderName);
                    FindInstalledEnvironments(request); // Not really using this yet, just putting in a call for testing
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(string.Format("Unexpected Exception thrown in '{0}::GetInstalledPackages' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
            }
        }    }
}
