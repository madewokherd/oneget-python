using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using OneGet.Sdk;
using Newtonsoft.Json.Linq;

namespace PythonProvider
{
    class PyPI
    {
        private static ConcurrentDictionary<Tuple<string, string, string, string>, JObject> detailed_info_cache = new ConcurrentDictionary<Tuple<string, string, string, string>, JObject>();

        public static IEnumerable<Tuple<string, string>> GetSources(Request request)
        {
            // FIXME: Add source management
            yield return new Tuple<string,string>("PyPI", "https://pypi.python.org/pypi");
        }

        private static WebResponse DoWebRequest(Tuple<string, string> source, byte[] call_xml, Request request)
        {
            WebRequest result = WebRequest.Create(source.Item2);
            result.Method = "POST";
            result.ContentType = "text/xml";
            result.ContentLength = call_xml.Length;
            Stream stream = result.GetRequestStream();
            stream.Write(call_xml, 0, call_xml.Length);
            stream.Close();
            return result.GetResponse();
        }

        private static JObject GetDetailedPackageInfo(Tuple<string, string> source, string name, string version, Request request)
        {
            JObject result;
            var key = new Tuple<string, string, string, string>(source.Item1, source.Item2, name, version);
            if (detailed_info_cache.TryGetValue(key, out result))
                return result;
            // Using JSON api here because it provides more info in one request than xmlrpc
            string uri = String.Format("{0}/{1}/{2}/json", source.Item2, Uri.EscapeUriString(name), Uri.EscapeUriString(version));
            request.Debug("FETCHING: {0}", uri);
            WebResponse response = WebRequest.Create(uri).GetResponse();
            HttpWebResponse httpresponse = response as HttpWebResponse;
            StreamReader reader = new StreamReader(response.GetResponseStream());
            string json = reader.ReadToEnd();
            reader.Close();
            result = JObject.Parse(json);
            detailed_info_cache.TryAdd(key, result);
            return result;
        }

        private static PackageDownload[] ParseUrls(JToken token)
        {
            JArray urls = (JArray)token;
            PackageDownload[] result = new PackageDownload[urls.Count];
            for (int i = 0; i < urls.Count; i++)
            {
                JObject uri = (JObject)urls[i];
                result[i].url = uri["url"].ToString();
                result[i].basename = uri["filename"].ToString();
                result[i].md5_digest = uri["md5_digest"].ToString();
                result[i].packagetype = uri["packagetype"].ToString();
                result[i].size = (long)uri["size"];
            }
            return result;
        }

        public static PythonPackage GetPackage(Tuple<string, string> source, string name, string version, Request request)
        {
            var detailed_info = GetDetailedPackageInfo(source, name, version, request);
            PythonPackage package = new PythonPackage(name);
            package.version = new VersionIdentifier(version);
            package.summary = detailed_info["info"]["summary"].ToString();
            package.source = source.Item1;
            package.sourceurl = source.Item2;
            package.search_key = name;
            package.downloads = ParseUrls(detailed_info["urls"]);
            JToken requires_dist;
            if (((JObject)detailed_info["info"]).TryGetValue("requires_dist", out requires_dist))
            {
                foreach (var requirement in requires_dist)
                {
                    package.requires_dist.Add(DistRequirement.Parse(requirement.ToString()));
                }
            }
            if (((JObject)detailed_info["info"]).TryGetValue("requires", out requires_dist))
            {
                foreach (var requirement in requires_dist)
                {
                    package.requires_dist.Add(DistRequirement.Parse(requirement.ToString()));
                }
            }
            return package;
        }

        private static IEnumerable<PythonPackage> FilterPackageVersions(Tuple<string,string> source,
            string search_name, string package_name, HashSet<string> nonhidden_versions,
            VersionIdentifier required, VersionIdentifier minimum, VersionIdentifier maximum,
            bool no_filter, Request request)
        {
            var detailed_info = GetDetailedPackageInfo(source, package_name, nonhidden_versions.ElementAt(0), request);
            bool list_all_versions = (no_filter || request.GetOptionValue("AllVersions") == "True");
            var release_listing = detailed_info.GetValue("releases") as JObject;
            List<string> sorted_versions = new List<string>();
            
            foreach (var release in release_listing)
            {
                sorted_versions.Add(release.Key);
            }

            sorted_versions.Sort(delegate(string a, string b)
            {
                // sort nonhidden versions first
                if (nonhidden_versions.Contains(a))
                {
                    if (!nonhidden_versions.Contains(b))
                        return -1;
                }
                else if (!nonhidden_versions.Contains(a))
                {
                    if (nonhidden_versions.Contains(b))
                        return 1;
                }
                // sort non-prerelease versions first
                VersionIdentifier va = new VersionIdentifier(a);
                VersionIdentifier vb = new VersionIdentifier(b);
                if (va.IsPrerelease && !vb.IsPrerelease)
                    return 1;
                if (!va.IsPrerelease && vb.IsPrerelease)
                    return -1;
                // newer versions first
                return vb.Compare(va);
            });

            foreach (var version in sorted_versions)
            {
                VersionIdentifier candidate_version = new VersionIdentifier(version);
                var uris = release_listing[version] as JArray;
                if (uris == null || uris.Count == 0)
                    continue;
                if (required != null && required.Compare(candidate_version) != 0)
                    continue;
                if (minimum != null && minimum.Compare(candidate_version) > 0)
                    continue;
                if (maximum != null && maximum.Compare(candidate_version) < 0)
                    continue;
                // FIXME: Mark this package as incomplete, fetch the proper package when installing or checking deps.
                PythonPackage package = new PythonPackage(package_name);
                package.version = new VersionIdentifier(version);
                package.summary = detailed_info["info"]["summary"].ToString();
                package.source = source.Item1;
                package.sourceurl = source.Item2;
                package.search_key = search_name;
                package.downloads = ParseUrls(uris);
                yield return package;
                if (!list_all_versions)
                    break;
            }
        }

        private static IEnumerable<PythonPackage> ContainsSearch(string name, string requiredVersion, string minimumVersion, string maximumVersion, Request request)
        {
            VersionIdentifier required=null, minimum=null, maximum=null;

            if (!string.IsNullOrWhiteSpace(requiredVersion))
                required = new VersionIdentifier(requiredVersion);
            if (!string.IsNullOrWhiteSpace(minimumVersion))
                minimum = new VersionIdentifier(minimumVersion);
            if (!string.IsNullOrWhiteSpace(maximumVersion))
                maximum = new VersionIdentifier(maximumVersion);

            MemoryStream call_ms = new MemoryStream();
            XmlWriter writer = XmlWriter.Create(call_ms);
            writer.WriteStartElement("methodCall");
            writer.WriteElementString("methodName", "search");
            writer.WriteStartElement("params");
            writer.WriteStartElement("param"); //spec
            writer.WriteStartElement("value");
            writer.WriteStartElement("struct");
            if (!string.IsNullOrEmpty(name))
            {
                writer.WriteStartElement("member");
                writer.WriteElementString("name", "name");
                writer.WriteStartElement("value");
                writer.WriteElementString("string", name);
                writer.WriteEndElement(); //value
                writer.WriteEndElement(); //member
            }
            // FIXME: Provide request options to also search summary, description, and keywords?
            writer.WriteEndElement(); //struct
            writer.WriteEndElement(); //value
            writer.WriteEndElement(); //param
            writer.WriteEndElement(); //params
            writer.WriteEndElement(); //methodCall
            writer.Close();

            byte[] call = call_ms.ToArray();

            foreach (var source in GetSources(request))
            {
                if (request.IsCanceled)
                    break;
                request.Debug("Python::Search asking {0}", source.Item1);
                using (var response = DoWebRequest(source, call, request))
                {
                    var search_response = ParseResponse(response.GetResponseStream(), request) as List<object>;
                    if (search_response == null)
                    {
                        request.Debug("search returned unexpected value");
                        continue;
                    }
                    string package_name = null;
                    HashSet<string> nonhidden_versions = new HashSet<string>();
                    foreach (var package_info_obj in search_response)
                    {
                        if (request.IsCanceled)
                            break;
                        var package_info = package_info_obj as Dictionary<string, object>;
                        if (package_info == null)
                        {
                            request.Debug("search returned unexpected value in array");
                            continue;
                        }
                        if (package_name != null && package_name != package_info["name"].ToString())
                        {
                            foreach (var package in FilterPackageVersions(source, name, package_name,
                                nonhidden_versions, required, minimum, maximum, false, request))
                                yield return package;
                            nonhidden_versions.Clear();
                        }
                        package_name = package_info["name"].ToString();
                        nonhidden_versions.Add(package_info["version"].ToString());
                    }
                    if (package_name != null)
                    {
                        foreach (var package in FilterPackageVersions(source, name, package_name,
                            nonhidden_versions, required, minimum, maximum, false, request))
                            yield return package;
                    }
                }
            }
        }

        private static IEnumerable<PythonPackage> ExactSearch(IEnumerable<string> names, string requiredVersion, string minimumVersion, string maximumVersion, bool no_filter, Request request)
        {
            VersionIdentifier required = null, minimum = null, maximum = null;

            if (!string.IsNullOrWhiteSpace(requiredVersion))
                required = new VersionIdentifier(requiredVersion);
            if (!string.IsNullOrWhiteSpace(minimumVersion))
                minimum = new VersionIdentifier(minimumVersion);
            if (!string.IsNullOrWhiteSpace(maximumVersion))
                maximum = new VersionIdentifier(maximumVersion);

            foreach (var source in GetSources(request))
            {
                foreach (string exact_name in names)
                {
                    MemoryStream call_ms = new MemoryStream();
                    XmlWriter writer = XmlWriter.Create(call_ms);
                    writer.WriteStartElement("methodCall");
                    writer.WriteElementString("methodName", "package_releases");
                    writer.WriteStartElement("params");
                    writer.WriteStartElement("param"); //package_name
                    writer.WriteStartElement("value");
                    writer.WriteElementString("string", exact_name);
                    writer.WriteEndElement(); //value
                    writer.WriteEndElement(); //param
                    writer.WriteEndElement(); //params
                    writer.WriteEndElement(); //methodCall
                    writer.Close();

                    byte[] call = call_ms.ToArray();

                    using (var response = DoWebRequest(source, call, request))
                    {
                        request.Debug("Python::Search asking {0} about {1}", source.Item1, exact_name);
                        var search_response = ParseResponse(response.GetResponseStream(), request) as List<object>;
                        if (search_response == null || search_response.Count == 0)
                        {
                            //FIXME: Check for non-hidden versions if this fails?
                            continue;
                        }
                        HashSet<string> versions = new HashSet<string>();
                        foreach (var version in search_response)
                        {
                            versions.Add((string)version);
                        }
                        foreach (var package in FilterPackageVersions(source, exact_name, exact_name,
                            versions, required, minimum, maximum, no_filter, request))
                            yield return package;
                    }
                }
            }
        }

        public static IEnumerable<PythonPackage> ExactSearch(string name, Request request)
        {
            return ExactSearch(new string[] { name }, null, null, null, true, request);
        }

        public static IEnumerable<PythonPackage> Search(string name, string requiredVersion, string minimumVersion, string maximumVersion, Request request)
        {
            bool exact_match = false;

            if (string.IsNullOrWhiteSpace(name) || name.ToLowerInvariant() != "python")
            {
                foreach (var package in ExactSearch(request.GetOptionValues("Name"), requiredVersion, minimumVersion, maximumVersion, false, request))
                {
                    exact_match = true;
                    yield return package;
                }

                if (!exact_match)
                {
                    foreach (var package in ContainsSearch(name, requiredVersion, minimumVersion, maximumVersion, request))
                        yield return package;
                }
            }
        }

        private static Dictionary<string, object> ParseStruct(XmlReader reader)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            reader.ReadStartElement("struct");
            while (reader.IsStartElement("member"))
            {
                reader.Read();
                if (!reader.IsStartElement("name"))
                    throw new Exception("unexpected xml");
                string name = reader.ReadElementContentAsString();
                if (!reader.IsStartElement("value"))
                    throw new Exception("unexpected xml");
                object value = ParseValue(reader);
                result[name] = value;
                reader.ReadEndElement(); //member
            }
            reader.ReadEndElement(); //struct
            return result;
        }

        private static List<object> ParseArray(XmlReader reader)
        {
            List<object> result = new List<object>();
            reader.ReadStartElement("array");
            reader.ReadStartElement("data");
            while (reader.IsStartElement("value"))
            {
                result.Add(ParseValue(reader));
            }
            reader.ReadEndElement(); //data
            reader.ReadEndElement(); //array
            return result;
        }

        private static object ParseValue(XmlReader reader)
        {
            object result;
            reader.ReadStartElement("value");
            if (!reader.IsStartElement())
            {
                throw new Exception("unexpected xml");
            }
            switch (reader.Name)
            {
                case "string":
                    result = reader.ReadElementContentAsString();
                    break;
                case "int":
                case "i4":
                    result = reader.ReadElementContentAsInt();
                    break;
                case "struct":
                    result = ParseStruct(reader);
                    break;
                case "array":
                    result = ParseArray(reader);
                    break;
                case "boolean":
                    result = (reader.ReadElementContentAsInt() != 0);
                    break;
                case "nil":
                    reader.ReadElementContentAsString();
                    result = null;
                    break;
                default:
                    throw new Exception(string.Format("unhandled value type {0}", reader.Name));
            }
            reader.ReadEndElement(); //value
            return result;
        }

        private static object ParseResponse(Stream stream, Request request)
        {
            XmlReader reader = XmlReader.Create(stream);
            reader.ReadStartElement("methodResponse");
            if (!reader.IsStartElement())
            {
                request.Debug("got unexpected xml-rpc response data");
                return null;
            }
            if (reader.Name == "fault")
            {
                // FIXME: learn to parse these
                request.Debug("got fault response from xml-rpc");
                return null;
            }
            reader.ReadStartElement("params");
            reader.ReadStartElement("param");
            object result = ParseValue(reader);
            reader.ReadEndElement(); //param
            reader.ReadEndElement(); //params
            reader.ReadEndElement(); //methodResponse
            return result;
        }
    }
}
