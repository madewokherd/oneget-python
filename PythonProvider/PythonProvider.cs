using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using OneGet.Sdk;

namespace PythonProvider
{
    public class PythonProvider
    {
        const string ProviderName = "Python";

        public void InitializeProvider(Request request)
        {
            request.Debug("Calling '{0}::InitializeProvider'", ProviderName);
        }

        public string GetPackageProviderName()
        {
            return ProviderName;
        }

        public void OnUnhandledException(string methodName, Exception exception)
        {
            Console.Error.WriteLine("Unexpected Exception thrown in '{0}::{1}' -- {2}\\{3}\r\n{4}", ProviderName, methodName, exception.GetType().Name, exception.Message, exception.StackTrace);
        }

        public void GetDynamicOptions(string category, Request request)
        {
            request.Debug("Calling '{0}::GetDynamicOptions'", ProviderName);
            switch ((category ?? "").ToLowerInvariant())
            {
                case "install":
                    request.YieldDynamicOption("PythonVersion", "String", false);
                    request.YieldDynamicOption("PythonLocation", "Folder", false);
                    break;
                case "provider":
                    break;
                case "source":
                    break;
                case "package":
                    break;
            }
        }

        private IEnumerable<PythonPackage> SearchSiteFolder(string path, PythonInstall install, Request request)
        {
            request.Debug("Python::SearchSiteFolder searching {0}", path);
            foreach (string dir in Directory.EnumerateDirectories(path, "*.dist-info"))
            {
                request.Debug("Python::SearchSiteFolder trying {0}", dir);
                PythonPackage result = PythonPackage.FromDistInfo(dir, install, request);
                if (result != null)
                    yield return result;
            }
        }

        public void GetInstalledPackages(string name, Request request)
        {
            request.Debug("Calling '{0}::GetInstalledPackages'", ProviderName);
            foreach (var install in PythonInstall.FindEnvironments(request))
            {
                foreach (var package in SearchSiteFolder(install.GlobalSiteFolder(), install, request))
                {
                    if (string.IsNullOrEmpty(name) || package.MatchesName(name, request))
                        package.YieldSelf(request);
                }
            }
        }

        public void FindPackage(string name, string requiredVersion, string minimumVersion, string maximumVersion, int id, Request request)
        {
            request.Debug("Calling '{0}::FindPackage'", ProviderName);
            foreach (var package in PyPI.Search(name, requiredVersion, minimumVersion, maximumVersion, request))
            {
                package.YieldSelf(request);
            }
        }
    }
}
