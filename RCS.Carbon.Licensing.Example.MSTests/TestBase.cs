using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RCS.Carbon.Shared;

namespace RCS.Carbon.Licensing.Example.MSTests;

public class TestBase
{
	protected void Sep1(string title)
	{
		int len = title.Length + 4;
		Info("┌" + new string('─', len) + "┐");
		Info("│  " + title + "  │");
		Info("└" + new string('─', len) + "┘");
	}

	protected void DumpNodes(IEnumerable<GenNode> nodes)
	{
		foreach (var node in GenNode.WalkNodes(nodes))
		{
			string pfx = string.Join("", Enumerable.Repeat("|  ", node.Level));
			Info($"{pfx}{node}");
		}
	}

	protected void Info(string message) => System.Diagnostics.Trace.WriteLine(message);

	protected void PrintJson(object value)
	{
		string json = JsonSerializer.Serialize(value, new JsonSerializerOptions() { WriteIndented = true });
		Info(json);
	}
}
