using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using OneGet.Sdk;
using Newtonsoft.Json.Linq;

namespace PythonProvider
{
    class PyPI
    {
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

        private static JObject GetDetailedPackageInfo(Tuple<string, string> source, string name, string version)
        {
            // Using JSON api here because it provides more info in one request than xmlrpc
            string uri = String.Format("{0}/{1}/{2}/json", source.Item2, Uri.EscapeUriString(name), Uri.EscapeUriString(version));
            WebResponse response = WebRequest.Create(uri).GetResponse();
            HttpWebResponse httpresponse = response as HttpWebResponse;
            StreamReader reader = new StreamReader(response.GetResponseStream());
            string json = reader.ReadToEnd();
            reader.Close();
            return JObject.Parse(json);
        }

        public static IEnumerable<PythonPackage> Search(string name, Request request)
        {
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
                    foreach (var package_info_obj in search_response)
                    {
                        var package_info = package_info_obj as Dictionary<string, object>;
                        if (package_info == null)
                        {
                            request.Debug("search returned unexpected value in array");
                            continue;
                        }
                        string package_name = package_info["name"].ToString();
                        string package_current_version = package_info["version"].ToString();
                        var detailed_info = GetDetailedPackageInfo(source, package_name, package_current_version);
                        var uri_listing = detailed_info.GetValue("urls") as JArray;
                        if (uri_listing == null || uri_listing.Count == 0)
                        {
                            request.Debug("package {0} current version has no urls", package_name);
                            continue;
                        }
                        PythonPackage package = new PythonPackage(package_name);
                        package.version = package_current_version;
                        package.summary = package_info["summary"].ToString();
                        package.source = source.Item1;
                        package.search_key = name;
                        yield return package;
                        if (request.IsCanceled)
                            break;
                    }
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
