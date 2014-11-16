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

        public void GetInstalledPackages(string name, object requestObject)
        {
            try
            {
                using (var request = requestObject.As<Request>())
                {
                    request.Debug("Calling '{0}::GetInstalledPackages'", ProviderName);
                    PythonInstall.FindEnvironments(request); // Not really using this yet, just putting in a call for testing
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(string.Format("Unexpected Exception thrown in '{0}::GetInstalledPackages' -- {1}\\{2}\r\n{3}"), ProviderName, e.GetType().Name, e.Message, e.StackTrace);
            }
        }    }
}
