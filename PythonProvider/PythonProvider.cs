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
            using (var request = requestObject.As<Request>())
            {
                try
                {
                    request.Debug("Calling '{0}::InitializeProvider'", ProviderName);
                }
                catch (Exception e)
                {
                    request.Debug(string.Format("Unexpected Exception thrown in '{0}::InitializeProvider' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
                }
            }
        }

        public string GetPackageProviderName()
        {
            return ProviderName;
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

        public void GetInstalledPackages(string name, object requestObject)
        {
            using (var request = requestObject.As<Request>())
            {
                try
                {
                    request.Debug("Calling '{0}::GetInstalledPackages'", ProviderName);
                    foreach (var install in PythonInstall.FindEnvironments(request))
                    {
                        foreach (var package in SearchSiteFolder(install.GlobalSiteFolder(), install, request))
                        {
                            if (name.IsEmptyOrNull() || package.MatchesName(name, request))
                                package.YieldSelf(request);
                        }
                    }
                }
                catch (Exception e)
                {
                    request.Debug(string.Format("Unexpected Exception thrown in '{0}::GetInstalledPackages' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
                }
            }
        }

        public void FindPackage(string name, string requiredVersion, string minimumVersion, string maximumVersion, int id, object requestObject)
        {
            using (var request = requestObject.As<Request>())
            {
                request.Debug("Calling '{0}::FindPackage'", ProviderName);
                try
                {
                    foreach (var package in PyPI.Search(name, request))
                    {
                        if (!string.IsNullOrEmpty(requiredVersion) && package.version != requiredVersion)
                            continue;
                        // FIXME: Do version comparison with minimumVersion/maximumVersion
                        package.YieldSelf(request);
                    }
                }
                catch (Exception e)
                {
                    request.Debug("Unexpected Exception thrown in '{0}::FindPackage' -- {1}\\{2}\r\n{3}", ProviderName, e.GetType().Name, e.Message, e.StackTrace);
                }
            }
        }    }
}
