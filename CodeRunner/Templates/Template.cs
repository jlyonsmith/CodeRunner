/// <script>
///  <vs>%ScriptVsVersion%</vs>
///  <clr>%ScriptClrVersion%</clr>
///  <fx>%ScriptFxVersion%</fx>
///  <refs>
%ScriptReferencesComment%
///  </refs>
/// </script>

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CodeRunner;
using ToolBelt;

public class Program
{
	public static int Main(string[] args)
	{
		if (args.Length == 0)
		{
			Console.WriteLine("Usage: {0}", ScriptEnvironment.ScriptPath.FileAndExtension);
			return 0;
		}

		return 0;
	}
}
