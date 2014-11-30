using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using OneGet.ProviderSDK;

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
            if (!name.IsEmptyOrNull())
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
                    XmlReader reader = XmlReader.Create(response.GetResponseStream());
                    reader.ReadStartElement("methodResponse");
                    if (!reader.IsStartElement())
                    {
                        request.Debug("Python::Search got unexpected response data");
                        continue;
                    }
                    if (reader.Name == "fault")
                    {
                        // FIXME: learn to parse these
                        request.Debug("Python::Search got fault response");
                        continue;
                    }
                    reader.ReadStartElement("params");
                    reader.ReadStartElement("param");
                    reader.ReadStartElement("value");
                    reader.ReadStartElement("array");
                    reader.ReadStartElement("data");
                    while (reader.IsStartElement("value"))
                    {
                        Dictionary<string, object> package_info = (Dictionary<string, object>)ParseValue(reader);
                        PythonPackage package = new PythonPackage(package_info["name"].ToString());
                        package.version = package_info["version"].ToString();
                        package.summary = package_info["summary"].ToString();
                        package.source = source.Item1;
                        package.search_key = name;
                        yield return package;
                        if (request.IsCanceled)
                            break;
                    }
                    if (request.IsCanceled)
                        // Avoid exceptions when we cancel during package listing
                        break;
                    reader.ReadEndElement(); //data
                    reader.ReadEndElement(); //array
                    reader.ReadEndElement(); //value
                    reader.ReadEndElement(); //param
                    reader.ReadEndElement(); //params
                    reader.ReadEndElement(); //methodResponse
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

        private static object ParseValue(XmlReader reader)
        {
            object result;
            reader.ReadStartElement("value");
            if (!reader.IsStartElement())
            {
                throw new Exception("unexpected xml");
            }
            if (reader.Name == "string")
                result = reader.ReadElementContentAsString();
            else if (reader.Name == "int" || reader.Name == "i4")
                result = reader.ReadElementContentAsInt();
            else if (reader.Name == "struct")
                result = ParseStruct(reader);
            else
                throw new Exception(string.Format("unhandled value type {0}", reader.Name));
            reader.ReadEndElement(); //value
            return result;
        }
    }
}
