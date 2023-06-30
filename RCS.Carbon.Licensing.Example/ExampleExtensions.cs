using System.Xml.Linq;

namespace RCS.Carbon.Licensing.Example.Extensions;

public static class ExampleExtensions
{
	public static string Attr(this XElement e, string name) => (string)e.Attribute(name);

	public static string Elem(this XElement e, string name) => (string)e.Element(name);

	public static string Val(this XElement e) => (string)e;
}
