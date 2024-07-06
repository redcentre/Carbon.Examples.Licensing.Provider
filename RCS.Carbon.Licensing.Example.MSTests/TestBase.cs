using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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

	protected void Info(string message) => System.Diagnostics.Trace.WriteLine(message);

	protected void PrintJson(object value)
	{
		string json = JsonSerializer.Serialize(value, new JsonSerializerOptions() { WriteIndented = true });
		Info(json);
	}
}
