using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Xmp;
using System.IO.Packaging;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FileDataExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var filePath = "../../../Downloads/archive-backend.pdf";
            if(args.Length != 0)
                filePath = args[0];

            
            var result = new DLPPropsParser().Parse(filePath);
            foreach (var item in result)
            {
                Console.WriteLine($"{item.Key} : {item.Value}");
            }

            Console.ReadLine();
        }
    }

    public interface IPropsParser {
        Dictionary<string, string> Parse(string filePath);
        Dictionary<string, string> Parse(Stream stream);
    }

    public abstract class BasePropsParser : IPropsParser
    {
        public Dictionary<string, string> Parse(string filePath)
        {
            using(var fileStream = File.OpenRead(filePath)){
                return Parse(fileStream);
            }
        }

        public abstract Dictionary<string, string> Parse(Stream stream);
    }

    public class DLPPropsParser {
        private List<IPropsParser> parsers;
        private Dictionary<string, string> props;
        public DLPPropsParser()
        {
            props = new Dictionary<string, string>();
            parsers = new List<IPropsParser>();
            parsers.Add(new DocPropertiesParser());
            parsers.Add(new ImageXmpParser());
            parsers.Add(new PdfXmpParser());
        }

        public IDictionary<string, string> Parse(string filePath){
            foreach (var parser in parsers)
            {
                try{
                    var parsedProps = parser.Parse(filePath);
                    foreach(var parsedProp in parsedProps){
                        if(!props.ContainsKey(parsedProp.Key))
                            props.Add(parsedProp.Key, parsedProp.Value);
                    }
                }catch(Exception ex){
                    Console.WriteLine($"{parser.GetType()}: {ex.Message}");
                }
            }
            return props;
        }

        public IDictionary<string, string> Parse(Stream stream){
            var props = new Dictionary<string, string>();
            foreach (var parser in parsers)
            {
                try{
                    props.Union(parser.Parse(stream));
                }catch(Exception ex){
                    Console.WriteLine(ex);
                }
            }
            return props;
        }
    }

    public class DocPropertiesParser : BasePropsParser
    {
        public override Dictionary<string, string> Parse(Stream stream)
        {
            var mappings = new Dictionary<string, string>();
            using(var package = Package.Open(stream)){
                var part = package.GetPart(new Uri("/docProps/custom.xml", UriKind.Relative));
                var partXml = XDocument.Load(part.GetStream());
                    foreach(var element in partXml?.Root?.Elements()){
                        var key = element.Attributes()?.FirstOrDefault(x => x.Name == "name")?.Value;
                        var value = element.Elements()?.FirstOrDefault()?.Value;
                        if(key != null)
                            mappings.Add(key, value);
                    }            
            }

            return mappings;
        }
    }

    public class ImageXmpParser : BasePropsParser
    {
        public override Dictionary<string, string> Parse(Stream stream)
        {
            var mappings = new Dictionary<string, string>();
            var xmpDirectory = ImageMetadataReader.ReadMetadata(stream).OfType<XmpDirectory>().FirstOrDefault();
            foreach (var property in xmpDirectory.XmpMeta.Properties){
                var key = property.Path?.Substring(property.Path.IndexOf(':') + 1);
                var value = property.Value;
                if(key != null)
                    mappings.Add(key, value);
            }
            return mappings;
        }
    }

    public class PdfXmpParser : BasePropsParser
    {
        public override Dictionary<string, string> Parse(Stream stream)
        {
            var props = new Dictionary<string, string>(); 
            byte[] bts;
            using(var ms = new MemoryStream())
            {
                byte[] buffer = new byte[16 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                bts = ms.ToArray();
            }
            var contentString = Encoding.UTF8.GetString(bts);
            contentString = contentString.Replace("\n", "").Replace("\r", "");
            var xmpPacketCaptures = Regex.Match(contentString, "\\<\\?xpacket\\sbegin.*?<?xpacket end.*?>");
            if(xmpPacketCaptures.Captures.Count == 0)
                return props;
            var packetString = xmpPacketCaptures.Captures.FirstOrDefault()?.Value;
            if(packetString == null)
                return props;
            var packetXml = XElement.Parse(packetString);
            var decs = packetXml.Descendants();
            var decendants = packetXml.Descendants()?.Cast<XElement>().Where(x => x.FirstNode.NodeType.ToString() == "Text");
            foreach (var decendant in decendants)
            {
                props.Add(decendant.Name.LocalName, ((XText)decendant.FirstNode).Value);
            }
            return props;
        }
    }

}

