using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using ToolBelt;

namespace CodeRunner
{
    public class Program
    {
        [LoaderOptimization(LoaderOptimization.MultiDomain)]
        [STAThread()]
        public static int Main(string[] args)
        {
#if DEBUG
            // This is useful in debugging unit tests that run an instance of scaffold.exe
            if (File.Exists("scaffold.exe.breakonentry.flag"))
                Debugger.Break();
#endif

            ScaffoldTool scaffold = new ScaffoldTool(new ConsoleOutputter());

            // Get any settings from the .config file
            if (!((IProcessConfiguration)scaffold).ProcessConfiguration(ConfigurationUserLevel.None))
            {
                return 1;
            }

            // Get any environment variable settings
            if (!((IProcessEnvironment)scaffold).ProcessEnvironment())
            {
                return 1;
            }

            // Get all the command line arguments
            if (!((IProcessCommandLine)scaffold).ProcessCommandLine(args))
            {
                return 1;
            }

            try
            {
                scaffold.Execute();
            }
            catch (Exception e)
            {
                // Log any exceptions that slip through, typically happens when 
                // debugging or if the Execute method cannot be jitted.
                scaffold.Output.Error(e.ToString());
                return 1;
            }

            return 0;
        }
    }
}
