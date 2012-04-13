using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CSharp;
using System.Collections.Generic;
using ToolBelt;
using System.Runtime.InteropServices;

namespace CodeRunner
{
    /// <summary>
    /// Code runner build task
    /// </summary>
    [CommandLineCopyright("CopyrightJohnLyonSmith")]
    [CommandLineTitle("CSharpCodeRunner")]
    [CommandLineDescription("CommandLineDescription")]
    public class CsrTool : ITool, IProcessCommandLine, IProcessEnvironment, IProcessConfiguration
    {
        #region Private Fields
        private bool runningFromCommandLine = false;
        private bool debugMessages = false;
        private bool stackTraces = false;
        private bool searchSystemPath = true;
        private bool allowUnsafeCode = false;
        private CommandLineParser parser = null;
        private static readonly string csrEnvironmentVar = "CSR_CONFIG";
        private ParsedPath scriptPath;
        private ParsedPath temporaryFileDirectory;
        private OutputHelper output;
        
        #endregion

        #region Private Properties
        internal CommandLineParser Parser 
        { 
            get 
            { 
                if (parser == null)
                    parser = new CommandLineParser(typeof(CsrTool), typeof(CsrResources), CommandLineParserFlags.None);
                
                return parser;
            }
        }

        public OutputHelper Output 
        { 
            get { return this.output; }
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// Show help
        /// </summary>
        [CommandLineArgument("help", Description = "HelpSwitchDescription", ShortName = "?")]
        public bool ShowHelp { get; set; }

        [DefaultCommandLineArgument("script", ValueHint = "ScriptSwitchHint")]
        public ParsedPath ScriptPath { get { return scriptPath; } set { scriptPath = value; } }

        /// <summary>
        /// Script arguments
        /// </summary>
        [UnprocessedCommandLineArgument("arguments", ValueHint = "ArgumentSwitchHint")]
        public string[] Arguments { get; set; }

        /// <summary>
        /// Display debug messages
        /// </summary>
        public bool DebugMessages { get { return debugMessages; } set { debugMessages = value; } }

        /// <summary>
        /// Display line numbers in error messages
        /// </summary>
        public bool StackTraces { get { return stackTraces; } set { stackTraces = value; } }

        /// <summary>
        /// Search system path for script if path not fully qualified
        /// </summary>
        public bool SearchSystemPath { get { return searchSystemPath; } set { searchSystemPath = value; } }

        /// <summary>
        /// Allow unsafe code to be compiled into script
        /// </summary>
        public bool AllowUnsafeCode { get { return allowUnsafeCode; } set { allowUnsafeCode = value; } }
        
        /// <summary>
        /// Exit code from script
        /// </summary>
        public int ExitCode { get; set; }

        #endregion    
        
        #region Construction
        /// <summary>
        /// Default constructor
        /// </summary>
        public CsrTool()
        {
        }
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="outputter">IOutputter to use for the script</param>
        public CsrTool(IOutputter outputter)
        {
            this.output = new OutputHelper(outputter, CsrResources.ResourceManager);
        }

        #endregion    
        
        #region Public Methods
        /// <summary>
        /// Execute the task
        /// </summary>
        /// <returns>true if task succeeded, false otherwise</returns>
        public void Execute()
        {
            if (!runningFromCommandLine)
            {
                string arguments = Parser.Arguments;
                Output.Message(MessageImportance.Low, Parser.CommandName + (arguments.Length == 0 ? "" : " " + Parser.Arguments));
            }

            // If the help option appears anywhere, just show the help
            if (this.ShowHelp)
            {
                Output.Message(Parser.LogoBanner);
                Output.Message(Parser.Usage);
                Output.Message("");
                return;
            }

            // From this point on assume a bad exit code
            this.ExitCode = 1;

            // At this point, we need a source file (the default argument)
            if (this.ScriptPath == null)
            {
                Output.Error(CsrResources.NoScriptSpecified);
                return;
            }

            // Need a zero length array if no command line arguments given
            if (this.Arguments == null)
            {
                this.Arguments = new string[0];
            }

            // Before we fully qualify the file record whether it had any full or relative path part
            bool justFilenameGiven = this.ScriptPath.IsFilenameOnly;

            if (this.ScriptPath.File == String.Empty)
            {
                Output.Error(CsrResources.NoEmptyFileName);
                return;
            }
            else if (this.ScriptPath.HasWildcards)
            {
                Output.Error(CsrResources.NoScriptWildcards);
                return;
            }

            if (this.ScriptPath.Extension == String.Empty)
                this.ScriptPath = new ParsedPath(this.ScriptPath.VolumeDirectoryAndFile + ".csr", PathType.File);

            // Fully qualify the path based on current directory
            this.ScriptPath = this.ScriptPath.MakeFullPath();

            // Check that the source exists, and optionally search for it in the PATH
            if (!File.Exists(this.ScriptPath))
            {
                if (this.SearchSystemPath && justFilenameGiven)
                {
                    IList<ParsedPath> found = PathUtility.FindFileInPaths(
                        new ParsedPathList(System.Environment.GetEnvironmentVariable("PATH"), PathType.Directory), 
                        this.ScriptPath.FileAndExtension);

                    if (found.Count > 0)
                    {
                        this.ScriptPath = new ParsedPath(found[0], PathType.File);

                        if (this.DebugMessages)
                            Output.Message(MessageImportance.Low,
                                CsrResources.ScriptFoundInPath(this.ScriptPath.FileAndExtension, this.ScriptPath.VolumeAndDirectory));
                    }
                    else
                    {
                        Output.Error(CsrResources.ScriptNotFoundInDirectoryOrPath(this.ScriptPath.FileAndExtension, this.ScriptPath.VolumeAndDirectory));
                        return;
                    }
                }
                else
                {
                    Output.Error(CsrResources.ScriptNotFound(this.ScriptPath));
                    return;
                }
            }

            // Set publicly visible script path (in this AppDomain)
            ScriptEnvironment.ScriptPath = this.ScriptPath;

            // Now we have a valid script file, go an extract the details
            ScriptInfo scriptInfo = null;

            try
            {
                scriptInfo = ScriptInfo.GetScriptInfo(this.ScriptPath);
            }
            catch (ScriptInfoException e)
            {
                Output.Error(e.Message);
                return;
            }

            IList<RuntimeInfo> runtimeInfos = RuntimeInfo.GetInstalledRuntimes();
            RuntimeInfo runtimeInfo = null;

            // Check to see that the scripts requested CLR & Fx are available.
            for (int i = 0; i < runtimeInfos.Count; i++)
            {
                if (runtimeInfos[i].ClrVersion == scriptInfo.ClrVersion && runtimeInfos[i].FxVersion == scriptInfo.FxVersion)
                {
                    runtimeInfo = runtimeInfos[i];
                    break;
                }
            }

            if (runtimeInfo == null)
            {
                Output.Error(CsrResources.ScriptsRequiredRuntimeAndFrameworkNotInstalled(scriptInfo.ClrVersion, scriptInfo.FxVersion));
                return;
            }

            IList<ParsedPath> references;

            try
            {
                references = ScriptInfo.GetFullScriptReferencesPaths(scriptPath, scriptInfo, runtimeInfo);
            }
            catch (ScriptInfoException e)
            {
                Output.Error(e.Message);
                return;
            }

            if (this.DebugMessages)
            {
                Output.Message(MessageImportance.Low, CsrResources.RuntimeVersion(
                    ProcessUtility.IsThis64BitProcess ? CsrResources.WordSize64 : CsrResources.WordSize32,
                    RuntimeEnvironment.GetSystemVersion().ToString()));
                Output.Message(MessageImportance.Low, CsrResources.ClrInstallPath(runtimeInfo.ClrInstallPath));
                Output.Message(MessageImportance.Low, CsrResources.NetFxInstallPath(runtimeInfo.FxInstallPath));
                Output.Message(MessageImportance.Low, CsrResources.NetFxReferenceAssemblyPath(runtimeInfo.FxReferenceAssemblyPath));
                Output.Message(MessageImportance.Low, CsrResources.TemporaryFileDirectory(TemporaryFileDirectory));

                foreach (var reference in references)
                {
                    Output.Message(MessageImportance.Low, CsrResources.Referencing(reference));
                }
            }

            try
            {
                IDictionary<string, string> providerOptions = new Dictionary<string, string>();
                
                // Setting the compiler version option should only be done under Fx 3.5 otherwise the option should be not present
                if (scriptInfo.FxVersion == "3.5")
                    providerOptions.Add("CompilerVersion", "v" + scriptInfo.FxVersion);
                
                CodeDomProvider provider = new CSharpCodeProvider(providerOptions);
                CompilerParameters parms = new CompilerParameters();

                // NOTE: When adding compiler options, don't forget to always add a space at end... <sigh/>

                StringBuilder compilerOptions = new StringBuilder();
                
                foreach (var reference in references)
                {
                    compilerOptions.AppendFormat("/r:\"{0}\" ", reference);
                }

                parms.CompilerOptions = compilerOptions.ToString();
                parms.GenerateExecutable = true;	// This doesn't mean generate a file on disk, it means generate an .exe not a .dll
                
                if (StackTraces)
                {
                    if (!Directory.Exists(TemporaryFileDirectory))
                        Directory.CreateDirectory(this.TemporaryFileDirectory);
                    
                    parms.OutputAssembly = TemporaryFileDirectory.VolumeAndDirectory + ScriptPath.File + ".exe";
                    parms.GenerateInMemory = false;
                    parms.IncludeDebugInformation = true;
                    parms.CompilerOptions += "/optimize- ";
                }
                else
                {
                    parms.GenerateInMemory = true;
                    parms.CompilerOptions += "/optimize+ ";
                }

                if (this.AllowUnsafeCode)
                    parms.CompilerOptions += "/unsafe+ ";

                if (DebugMessages)
                    Output.Message(MessageImportance.Low, "Compiling script file '{0}'", this.ScriptPath);
                    
                CompilerResults results = provider.CompileAssemblyFromFile(parms, this.ScriptPath);

                if (results.Errors.Count > 0)
                {
                    foreach (CompilerError error in results.Errors)
                    {
                        Console.Error.WriteLine(
							CsrResources.ErrorLine(error.FileName, error.Line, error.Column, error.ErrorNumber, error.ErrorText));
                    }
                    
                    return;
                }
                else
                {
                    // Take the compiled assembly and invoke it in this appdomain.
                    object ret = null;

                    ScriptEnvironment.FullyQualifiedReferences = references;

                    AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(OnAssemblyResolve);

                    // Get information about the entry point							
                    MethodInfo mainMethod = results.CompiledAssembly.EntryPoint;
                    ParameterInfo[] mainParams = mainMethod.GetParameters();
                    Type returnType = mainMethod.ReturnType;

                    try
                    {
                        if (returnType == typeof(void))
                        {
                            if (mainParams.Length > 0)
                                mainMethod.Invoke(null, new object[1] { this.Arguments });
                            else
                                mainMethod.Invoke(null, null);
                        }
                        else
                        {
                            if (mainParams.Length > 0)
                                ret = mainMethod.Invoke(null, new object[1] { this.Arguments });
                            else
                                ret = mainMethod.Invoke(null, null);
                        }
                    }
                    catch (Exception ex)  // Catch script errors
                    {
                        // When catching a script error here, the actual script exception will be the inner exception
                        Output.Error(String.Format(
							CsrResources.ExceptionFromScript(ex.InnerException != null ? ex.InnerException.Message : ex.Message)));

                        if (this.StackTraces && ex.InnerException != null)
                            Output.Error(ex.InnerException.StackTrace);

                        return;
                    }

                    if (ret != null && returnType == typeof(int))
                    {
                        this.ExitCode = (int)ret;
                        return;
                    }
                }
            }
            catch (Exception ex)  // Catch compilation exceptions
            {
                string message = CsrResources.ExceptionDuringCompile + ex.Message;

                if (ex.InnerException != null)
                    message += ex.InnerException.Message;

                Output.Error(message);
                return;
            }

            ExitCode = 0;
            
            return;
        }

        #endregion    

        #region Internal Static Methods
        internal static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Assembly resolver, used to load assemblies specified in the .rsp files or command line which would 
            // not normally be found because they are not in the GAC, csr.exe directory or cached image directory.

            string assemblyName = args.Name.Substring(0, args.Name.IndexOf(','));

            // Find the first assembly where the file names match
            foreach (ParsedPath path in ScriptEnvironment.FullyQualifiedReferences)
            {
                if (CaseInsensitiveComparer.Default.Compare(path.File.ToString(), assemblyName) == 0)
                    return Assembly.LoadFrom(path);
            }

            return null;
        }

        #endregion

        #region Private Properties
        private ParsedPath TemporaryFileDirectory
        {
            get
            {
                Random rand = new Random();

                if (temporaryFileDirectory == null)
                {
                    temporaryFileDirectory = new ParsedPath(Path.GetTempPath(), PathType.Directory)
                        .Append(String.Format(@"CodeRunner_{0:X8}\", rand.Next()), PathType.Directory);
                }

                return temporaryFileDirectory;
            }
        }

        #endregion

        #region Private Methods
        private static string CachifyScriptPath(ParsedPath scriptPath)
        {
            Debug.Assert(scriptPath.IsFullPath);

            StringBuilder sb = new StringBuilder(scriptPath.VolumeDirectoryAndFile + ".exe");

            sb.Replace('\\', '_');
            sb.Replace(':', '_');
            sb.Replace(' ', '_');

            return sb.ToString();
        }

        private void GetBoolSetting(CsrSection section, string setting, ref bool val)
        {
            try
            {
                for (int i = 0; i < section.Settings.Count; i++)
                {
                    TypeElement element = section.Settings[i];

                    if (String.Compare(element.Key, setting, true) == 0)
                    {
                        if (!TryParseTrueFalse(element.Value, ref val))
                            Output.Warning(CsrResources.ErrorParsingBooleanInConfig(element.Value, element.Key));
                        
                        return;
                    }
                }
            }
            catch (ConfigurationException e)
            {
                Output.Warning(e.Message);
            }
        }

        private void GetBoolSetting(IDictionary dict, string setting, ref bool val)
        {
            string temp = (string)dict[setting];

            if (!String.IsNullOrEmpty(temp))
            {
                if (!TryParseTrueFalse(temp, ref val))
                    Output.Warning(CsrResources.ErrorParsingBooleanInEnvironment(temp, setting, csrEnvironmentVar));
            }
        }
        
        private static bool TryParseTrueFalse(string s, ref bool b)
        {
            if ((String.Compare(s, CsrResources.No, true) == 0 ||
                String.Compare(s, CsrResources.False, true) == 0))
            {
                b = false;
                return true;
            }
            else if ((String.Compare(s, CsrResources.Yes, true) == 0 ||
                String.Compare(s, CsrResources.True, true) == 0))
            {
                b = true;
                return true;
            }
            
            return false;
        }

        #endregion

        #region IProcessCommandLine Members

        bool IProcessCommandLine.ProcessCommandLine(string[] args)
        {
            runningFromCommandLine = true;
            
            CommandLineParser parser = new CommandLineParser(typeof(CsrTool), typeof(CsrResources));

            try
            {
                parser.ParseAndSetTarget(args, this);
            }
            catch (CommandLineArgumentException e)
            {
                Output.Error(e.Message);
                return false;
            }

            return true;
        }

        #endregion

        #region IProcessEnvironment Members

        bool IProcessEnvironment.ProcessEnvironment()
        {
            string allVars = Environment.GetEnvironmentVariable(csrEnvironmentVar);
            
            if (allVars == null)
                return true;	
                
            string[] vars = allVars.Split(';');

            ListDictionary environ = new ListDictionary();
            
            if (vars.Length > 0)
            {
                foreach (string var in vars)
                {
                    string[] parts = var.Split('=');
                    
                    if (parts.Length == 1)
                    {
                        environ[parts[0].Trim()] = String.Empty;
                    }
                    else if (parts.Length == 2 && parts[0].Length > 0)
                    {
                        environ[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            
            if (environ.Count == 0)
                return true;

            GetBoolSetting(environ, "DebugMessages", ref debugMessages);
            GetBoolSetting(environ, "StackTraces", ref stackTraces);
            GetBoolSetting(environ, "SearchSystemPath", ref searchSystemPath);
            GetBoolSetting(environ, "AllowUnsafeCode", ref allowUnsafeCode);
            
            return true;
        }

        #endregion

        #region IProcessConfig Members

        bool IProcessConfiguration.ProcessConfiguration(ConfigurationUserLevel userLevel)
        {
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(userLevel);
                CsrSection section = null;
                
                if (!config.HasFile)
                    return true;
                
                try
                {
                    section = config.GetSection("csr") as CsrSection;
                }
                catch (ConfigurationException e)
                {
                    Output.Error("Problem loading program configuration - {0}", e.Message);
                }

                if (section == null)
                {
                    // This means that either there was no section or that it was improperly specified in
                    // the .config file.  Make sure that the configSection is at the top of the file.
                    return true;
                }

                GetBoolSetting(section, "DebugMessages", ref debugMessages);
                GetBoolSetting(section, "StackTraces", ref stackTraces);
                GetBoolSetting(section, "SearchSystemPath", ref searchSystemPath);
                GetBoolSetting(section, "AllowUnsafeCode", ref allowUnsafeCode);
            }
            catch (ConfigurationErrorsException)
            {
                Output.Warning("Application configuration could not be loaded");
                return false;
            }
            
            return true;
        }

        #endregion
    }
}
