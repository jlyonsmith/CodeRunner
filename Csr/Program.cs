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
            CsrTool csr = new CsrTool(new ConsoleOutputter());

            // Get any settings from the .config file
            if (!((IProcessConfiguration)csr).ProcessConfiguration(ConfigurationUserLevel.None))
            {
                return 1;
            }

            // Get any settings from the environment
            if (!((IProcessEnvironment)csr).ProcessEnvironment())
            {
                return 1;
            }

            // Get all the command line arguments
            if (!((IProcessCommandLine)csr).ProcessCommandLine(args))
            {
                return 1;
            }

            try
            {
                csr.Execute();
            }
            catch (Exception e)
            {
                // Log any exceptions that slip through. This will generally only
                // happen during debugging or if the Execute method cannot be jitted.
                csr.Output.Error(e.ToString());
                return 1;
            }

            return csr.ExitCode;
        }
    }
}
