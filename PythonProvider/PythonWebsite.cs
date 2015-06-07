using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using OneGet.Sdk;
using Newtonsoft.Json.Linq;

namespace PythonProvider
{
    class PythonWebsite
    {
        private static string api_url = "https://www.python.org/api/v1";

        private static JObject DoWebRequest(string url)
        {
            HttpWebRequest result = (HttpWebRequest)WebRequest.Create(url);
            result.Accept = "application/json";
            WebResponse response = result.GetResponse();
            StreamReader reader = new StreamReader(response.GetResponseStream());
            string json = reader.ReadToEnd();
            reader.Close();
            return JObject.Parse(json);
        }

        private static PythonInstall FromReleaseRes(JObject res, Request request)
        {
            string release_name = res["name"].ToString();
            PythonInstall result = new PythonInstall();
            result.version = new VersionIdentifier(release_name.Substring(7));
            var parts = res["resource_uri"].ToString().TrimEnd('/').Split('/');
            result.web_resource = parts[parts.Length - 1];
            result.source = "Python.org";
            return result;
        }

        private static bool BetterVersion(VersionIdentifier current, VersionIdentifier candidate)
        {
            if (current.IsPrerelease && !candidate.IsPrerelease)
                return true;
            if (!current.IsPrerelease && candidate.IsPrerelease)
                return false;
            return (candidate.Compare(current) > 0);
        }

        public static IEnumerable<PythonPackage> Search(string name, string requiredVersion, string minimumVersion, string maximumVersion, Request request)
        {
            if (string.IsNullOrWhiteSpace(name) || name.ToLowerInvariant() == "python")
            {
                VersionIdentifier required = string.IsNullOrEmpty(requiredVersion) ? null : new VersionIdentifier(requiredVersion);
                VersionIdentifier minimum = string.IsNullOrEmpty(minimumVersion) ? null : new VersionIdentifier(minimumVersion);
                VersionIdentifier maximum = string.IsNullOrEmpty(maximumVersion) ? null : new VersionIdentifier(maximumVersion);
                Dictionary<string, VersionIdentifier> best_versions = new Dictionary<string,VersionIdentifier>();
                Dictionary<string, JObject> best_releases = new Dictionary<string,JObject>();

                bool list_all_versions = (request.GetOptionValue("AllVersions") == "True");

                int filter_version = -1;
                if (required != null)
                    filter_version = required.release[0];

                string version_query_string = filter_version == -1 ? "" : string.Format("&version={0}", filter_version);

                string url = string.Format("{0}/downloads/release/?limit=0{1}", api_url, version_query_string);
                JObject result = DoWebRequest(url);

                foreach (JObject release in result["objects"])
                {
                    string release_name = release["name"].ToString();
                    if (!release_name.StartsWith("Python "))
                        continue;
                    var release_version = new VersionIdentifier(release_name.Substring(7));
                    if ((required == null || required.Compare(release_version) == 0) &&
                        (minimum == null || minimum.Compare(release_version) <= 0) &&
                        (maximum == null || maximum.Compare(release_version) >= 0))
                    {
                        if (list_all_versions)
                        {
                            yield return FromReleaseRes(release, request);
                        }
                        else
                        {
                            string major_version = release["version"].ToString();
                            VersionIdentifier current_best;
                            if (!best_versions.TryGetValue(major_version, out current_best) ||
                                BetterVersion(current_best, release_version))
                            {
                                best_versions[major_version] = release_version;
                                best_releases[major_version] = release;
                            }
                        }
                    }
                }

                if (!list_all_versions)
                {
                    foreach (var release in best_releases)
                    {
                        yield return FromReleaseRes(release.Value, request);
                    }
                }
            }
        }
    }
}
