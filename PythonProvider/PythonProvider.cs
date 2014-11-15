using System;
using System.Diagnostics;
using OneGet.ProviderSDK;
using IRequestObject = System.Object;

namespace PythonProvider
{
    public class PythonProvider
    {
        const string ProviderName = "Python";

        public void InitializeProvider(IRequestObject requestImpl)
        {
            try
            {
                using (var request = requestImpl.As<Request>())
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
        }    }
}
