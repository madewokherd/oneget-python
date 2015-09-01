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

        private static Dictionary<string, string[]> Features = new Dictionary<string, string[]> {
            { Constants.Features.SupportedExtensions, new[]{"whl"} },
            { Constants.Features.MagicSignatures, new[]{Constants.Signatures.Zip} },
        };

        public void GetFeatures(Request request)
        {
            request.Debug("Calling '{0}::GetFeatures' ", ProviderName);

            foreach (var feature in Features)
            {
                request.Yield(feature);
            }
        }

        public void OnUnhandledException(string methodName, Exception exception)
        {
            Console.Error.WriteLine("Unexpected Exception thrown in '{0}::{1}' -- {2}\\{3}\r\n{4}", ProviderName, methodName, exception.GetType().Name, exception.Message, exception.StackTrace);
        }

        public void GetDynamicOptions(string category, Request request)
        {
            request.Debug("Calling '{0}::GetDynamicOptions({1})'", ProviderName, category);
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
                    request.YieldDynamicOption("PythonVersion", "String", false);
                    break;
            }
        }

        public void GetInstalledPackages(string name, string requiredVersion, string minimumVersion, string maximumVersion, Request request)
        {
            request.Debug("Calling '{0}::GetInstalledPackages({1},{2},{3},{4})'", ProviderName, name, requiredVersion, minimumVersion, maximumVersion);
            VersionIdentifier required = string.IsNullOrEmpty(requiredVersion) ? null : new VersionIdentifier(requiredVersion);
            VersionIdentifier minimum = string.IsNullOrEmpty(minimumVersion) ? null : new VersionIdentifier(minimumVersion);
            VersionIdentifier maximum = string.IsNullOrEmpty(maximumVersion) ? null : new VersionIdentifier(maximumVersion);
            foreach (var install in PythonInstall.FindEnvironments(request))
            {
                if (string.IsNullOrEmpty(name) || name == "Python")
                    install.YieldSelf(request);
                foreach (var package in install.FindInstalledPackages(name, requiredVersion, request))
                {
                    if ((required == null || required.Compare(package.version) == 0) &&
                        (minimum == null || minimum.Compare(package.version) <= 0) &&
                        (maximum == null || maximum.Compare(package.version) >= 0))
                        package.YieldSelf(request);
                }
            }
        }

        public void FindPackage(string name, string requiredVersion, string minimumVersion, string maximumVersion, int id, Request request)
        {
            request.Debug("Calling '{0}::FindPackage'", ProviderName);
            foreach (var package in PythonWebsite.Search(name, requiredVersion, minimumVersion, maximumVersion, request))
            {
                package.YieldSelf(request);
            }
            foreach (var package in PyPI.Search(name, requiredVersion, minimumVersion, maximumVersion, request))
            {
                package.YieldSelf(request);
            }
        }

        public void FindPackageByFile(string file, int id, Request request)
        {
            request.Debug("Calling '{0}::FindPackageByFile' '{1}','{2}'", ProviderName, file, id);
            foreach (var package in PythonPackage.PackagesFromFile(file, request))
            {
                package.YieldSelf(request);
            }
        }

        public void InstallPackage(string fastPackageReference, Request request)
        {
            request.Debug("Calling '{0}::InstallPackage' '{1}'", ProviderName, fastPackageReference);
            var package = PythonPackage.FromFastReference(fastPackageReference, request);
            if (package is PythonInstall)
            {
                ((PythonInstall)package).Install(request);
                return;
            }
            List<PythonInstall> usableinstalls = new List<PythonInstall>();
            List<PythonInstall> unusableinstalls = new List<PythonInstall>();
            foreach (var candidateinstall in PythonInstall.FindEnvironments(request))
            {
                if (package.CanInstall(candidateinstall, request))
                {
                    usableinstalls.Add(candidateinstall);
                }
                else
                {
                    unusableinstalls.Add(candidateinstall);
                }
            }
            if (usableinstalls.Count == 1)
            {
                package.Install(usableinstalls[0], request);
            }
            else if (usableinstalls.Count == 0)
            {
                request.Error(ErrorCategory.NotImplemented, package.name, "TODO: bootstrap a python?");
            }
            else if (usableinstalls.Count > 1)
            {
                if (request.GetOptionValue("Force") == "True")
                {
                    PythonInstall greatest = usableinstalls[0];
                    foreach (var candidate in usableinstalls)
                    {
                        if (candidate.version.Compare(greatest.version) > 0)
                            greatest = candidate;
                    }
                    package.Install(greatest, request);
                }
                else
                {
                    request.Warning("Multiple installed Python interpreters could satisfy this request:");
                    foreach (var install in usableinstalls)
                    {
                        request.Warning("  Python version '{0}' at '{1}'", install.version, install.exe_path);
                    }
                    request.Warning("Please select a Python to install to, using e.g. -PythonVersion 3.2 or -PythonLocation c:\\python32\\python.exe");
                    request.Error(ErrorCategory.NotSpecified, package.name, "Not enough information to select a Python interpreter for the install");
                }
            }
        }

        public void UninstallPackage(string fastPackageReference, Request request)
        {
            var package = PythonPackage.FromFastReference(fastPackageReference, request);
            package.Uninstall(request);
        }
    }
}
