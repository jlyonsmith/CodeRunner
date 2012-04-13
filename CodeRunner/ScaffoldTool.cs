using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using Microsoft.Win32;
using ToolBelt;
using System.Collections.Generic;

namespace CodeRunner
{
    /// <summary>
    /// Scaffold task
    /// </summary>
    [CommandLineCopyright("CopyrightJohnLyonSmith")]
    [CommandLineTitle("ScaffoldTitle")]
    [CommandLineDescription("ScaffoldDescription")]
    public class ScaffoldTool : ITool, IProcessCommandLine, IProcessConfiguration, IProcessEnvironment
    {
        #region Private Fields
        private bool showHelp = false;
        private ParsedPath scriptPath = null;
        private ParsedPath programTemplate = null;
        private bool verbose = true;
        private ScriptLanguage language = ScriptLanguage.Unknown;
        private CommandLineParser parser = null;
        private bool runningFromCommandLine = false;
        private bool wait = false;
        private bool noRemoting = false;
        private string remotingUrl = null;
        private static readonly string scaffoldEnvironmentVar = "SCAFFOLD_CONFIG";
        private static readonly string scaffoldExe = "scaffold.exe";
        private OutputHelper output;

        #endregion

        #region Private Properties
        internal CommandLineParser Parser
        {
            get
            {
                if (parser == null)
                    parser = new CommandLineParser(typeof(ScaffoldTool), typeof(ScaffoldResources), CommandLineParserFlags.None);

                return parser;
            }
        }

        public OutputHelper Output 
        { 
            get { return output; }
        }

        #endregion

        #region Construction
        /// <summary>
        /// Defoult constructor
        /// </summary>
        public ScaffoldTool()
        {
        }
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="outputter">IOutputter to use</param>
        public ScaffoldTool(IOutputter outputter)
        {
            this.output = new OutputHelper(outputter, CsrResources.ResourceManager);
        }

        #endregion    

        #region Public Properties

        /// <summary>
        /// Show help for the task
        /// </summary>
        [CommandLineArgument("help", Description = "HelpSwitchDescription", ShortName = "?", ValueHint = "")]
        public bool ShowHelp
        {
            get
            {
                return showHelp;
            }
            set
            {
                showHelp = value;
            }
        }

        /// <summary>
        /// Verbose output
        /// </summary>
        [CommandLineArgument("verbose", Description = "VerboseSwitchDescription", ShortName = "v", ValueHint = "")]
        public bool Verbose
        {
            get
            {
                return verbose;
            }
            set
            {
                verbose = value;
            }
        }

        /// <summary>
        /// Wait for VS instance to finish before returning
        /// </summary>
        [CommandLineArgument("wait", Description = "WaitSwitchDescription", ShortName = "w", ValueHint = "")]
        public bool Wait
        {
            get { return wait; }
            set { wait = value; }
        }

        [CommandLineArgument("template", Description = "ScriptTemplateSwitchDescription", ShortName = "t", ValueHint = "ScriptTemplateSwitchHint")]
        public ParsedPath ScriptTemplate
        {
            get
            {
                return programTemplate;
            }
            set
            {
                programTemplate = value;
            }
        }
        
        [DefaultCommandLineArgument("script", ValueHint = "ScriptSwitchHint")]
        public ParsedPath ScriptPath
        {
            get
            {
                return scriptPath;
            }
            set
            {
                scriptPath = value;
                language = ScriptEnvironment.GetScriptLanguageFromExtension(scriptPath.Extension);
            }
        }

        /// <summary>
        /// Do not use remoting to start a hidden instance of scaffold
        /// </summary>
        public bool NoRemoting 
        { 
            set
            {
                noRemoting = value;
            }
            get
            {
                return noRemoting;
            } 
        }

        internal string Language
        {
            set
            {
                language = (ScriptLanguage)Enum.Parse(typeof(ScriptLanguage), value, true);
            }
        }

        internal string RemotingUrl 
        { 
            get { return remotingUrl; } 
            set { remotingUrl = value; } 
        } 

        internal ScriptLanguage _Language
        {
            get { return language; }
        }
        
        #endregion

        #region Private Methods

        private string GetResource(string s)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(s);
            StreamReader sr = new StreamReader(stream, Encoding.ASCII);

            return sr.ReadToEnd();
        }

        private void CreateFile(string fileName, string content)
        {
            if (File.Exists(fileName))
            {
                Output.Warning(ScaffoldResources.AlreadyExists(fileName));
                return;
            }

            using (StreamWriter sw = new StreamWriter(fileName, false, System.Text.Encoding.ASCII))
            {
                sw.Write(content);
            }

            if (this.Verbose)
                Output.Message(MessageImportance.Low, ScaffoldResources.Created(fileName));
        }

        private void DeleteDirectory(string dirName)
        {
            try
            {
                Directory.Delete(dirName, true);

                if (this.Verbose)
                    Output.Message(MessageImportance.Low, ScaffoldResources.SubDirDeleted(dirName));
            }
            catch (SystemException)
            {
                Output.Warning(ScaffoldResources.SubDirNotDeleted(dirName));
            }
        }

        private string CreateCommentSnippetFromReferences(IList<string> references)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < references.Count; i++)
            {
                string reference = references[i];

                // Important not to leave gap in the comment header...
                sb.AppendFormat(
                    "///   <ref>{0}</ref>{1}",
                    reference,
                    i < references.Count - 1 ? System.Environment.NewLine : String.Empty);
            }

            return sb.ToString();
        }

        private string CreateXmlSnippetFromReferences(IList<ParsedPath> paths)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ParsedPath path in paths)
            {
                sb.AppendFormat(
                    "  <Reference Include=\"{0}\">" + System.Environment.NewLine +
                    "    <HintPath>{1}</HintPath>" + System.Environment.NewLine +
                    "  </Reference>" + System.Environment.NewLine,
                    path.File,
                    path);
            }

            return sb.ToString();
        }

        private bool CreateProjectFiles(ScriptInfo scriptInfo, VisualStudioInfo vsInfo, RuntimeInfo runtimeInfo, ParsedPath projectDir)
        {
            if (!Directory.Exists(this.ScriptPath.VolumeAndDirectory))
            {
                Output.Error(ScaffoldResources.DirectoryDoesNotExist(this.ScriptPath.VolumeAndDirectory));
                return false;
            }
			
            Dictionary<string, string> dict = new Dictionary<string, string>();

            dict.Add("AssemblyName", this.ScriptPath.File);
            dict.Add("SourceDirectory", this.ScriptPath.VolumeAndDirectory);
            dict.Add("SourceName", this.ScriptPath.FileAndExtension);
            dict.Add("ProjectGuid", Guid.NewGuid().ToString().ToUpper());
			dict.Add("ProjectTypeExt", ScriptEnvironment.GetExtensionFromScaffoldLanguage(this.language));
            dict.Add("ProductVersion", String.Format("{0}.{1}", vsInfo.VsVersion, vsInfo.VsBuild));
			dict.Add("ToolsVersion", String.Format(" ToolsVersion=\"{0}\" ", scriptInfo.FxVersion));
			dict.Add("TargetFrameworkVersion", "v" + scriptInfo.FxVersion);
            dict.Add("ScriptVsVersion", scriptInfo.VsVersion);
            dict.Add("ScriptClrVersion", scriptInfo.ClrVersion);
            dict.Add("ScriptFxVersion", scriptInfo.FxVersion);

            switch (scriptInfo.VsVersion)
			{
                case "10.0":
				    dict.Add("SolutionFileVersion", "11.00");
				    dict.Add("VSName", "2010");
                    break;
			
                case "9.0":
                    dict.Add("SolutionFileVersion", "10.00");
                    dict.Add("VSName", "2008");
                    break;
                
                case "8.0":
                    dict.Add("ToolsVersion", "");
                    dict.Add("SolutionFileVersion", "9.00");
                    dict.Add("VSName", "2005");
                    break;
            }

            dict.Add("ScriptReferencesComment", CreateCommentSnippetFromReferences(scriptInfo.References));
            dict.Add("ReferenceXmlSnippet", CreateXmlSnippetFromReferences(ScriptInfo.GetFullScriptReferencesPaths(scriptPath, scriptInfo, runtimeInfo)));

            string tagProgramFile = this.ScriptTemplate;

            if (tagProgramFile == null || !File.Exists(tagProgramFile))
                tagProgramFile = GetResource("CodeRunner.Templates.Template.cs");

            string sourceFile = StringUtility.ReplaceTags(tagProgramFile, "%", "%", dict);

            string tagCsProjFile = GetResource("CodeRunner.Templates.Template.csproj");
            string csProjFile = StringUtility.ReplaceTags(tagCsProjFile, "%", "%", dict);

            string tagCsProjUserFile = GetResource("CodeRunner.Templates.Template.csproj.user");
            string csProjUserFile = StringUtility.ReplaceTags(tagCsProjUserFile, "%", "%", dict);

            string tagSlnFile = GetResource("CodeRunner.Templates.Template.sln");
            string slnFile = StringUtility.ReplaceTags(tagSlnFile, "%", "%", dict);

            try
            {
                if (!File.Exists(this.ScriptPath))
                    CreateFile(this.ScriptPath, sourceFile);

                CreateFile(projectDir.VolumeAndDirectory + this.ScriptPath.File + ".csproj", csProjFile);
                CreateFile(projectDir.VolumeAndDirectory + this.ScriptPath.File + ".csproj.user", csProjUserFile);
                CreateFile(projectDir.VolumeAndDirectory + this.ScriptPath.File + ".sln", slnFile);
            }
            catch (IOException exp)
            {
                Output.Error("{0}", exp.Message);
                return false;
            }

            return true;
        }

        private bool StartDevenvAndWait(VisualStudioInfo vsInfo, ParsedPath solutionFile)
        {
            // Now try and start devenv
            Process process = null;

            // Clear undocumented MS build environment variables that will confuse VS if set
            Environment.SetEnvironmentVariable("COMPLUS_INSTALLROOT", "");
            Environment.SetEnvironmentVariable("COMPLUS_VERSION", "");

            try
            {
                if (this.Verbose)
                    Output.Message(MessageImportance.Low, ScaffoldResources.StartingVS);

                ProcessStartInfo startInfo = new ProcessStartInfo(
                    vsInfo.DevEnvExe, "\"" + solutionFile + "\" \"" + this.ScriptPath + "\"");

                startInfo.WorkingDirectory = solutionFile.VolumeAndDirectory;
                startInfo.UseShellExecute = false;

                process = Process.Start(startInfo);
            }
            catch (Win32Exception e)
            {
                Output.Error(ScaffoldResources.UnableToStartVS, solutionFile, e.Message);
                return false;
            }

            // Devenv has started.  Wait for it to exit.
            if (process != null)
            {
                if (this.Verbose && this.Wait)
                    Output.Message(MessageImportance.Low, ScaffoldResources.WaitingForVS);

                // At this point we free the server/parent scaffold process so that it can 
                // exit and return control of the console to the user.  Any logging after this 
                // point might silently fail if the remote build engine decides to away, but that's OK.
                Output.Outputter.OutputCustomEvent(new RemoteOutputEventArgs(true));

                process.WaitForExit();
                process.Close();
                process = null;
            }
            else
            {
                Output.Error(ScaffoldResources.VSDidNotStart);
                return false;
            }

            return true;
        }

        private bool StartScaffoldAndRemote()
        {
            Process process = null;
            string programName;
            
            if (runningFromCommandLine)
                programName = Assembly.GetEntryAssembly().Location;
            else
            {
                // Look for scaffold.exe in the same directory as this assembly
                ParsedPath path = new ParsedPath(Assembly.GetExecutingAssembly().Location, PathType.File);
                
                programName = path.VolumeAndDirectory + scaffoldExe;
            }
            
            using (RemoteOutputter remoteOutputter = new RemoteOutputter(this.Output.Outputter))
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(programName, Parser.Arguments);

                    startInfo.UseShellExecute = false;
                    startInfo.EnvironmentVariables[scaffoldEnvironmentVar] = "RemotingUrl=" + remoteOutputter.RemotingUrl;

                    process = Process.Start(startInfo);
                    
                    if (process == null)
                    {
                        Output.Error(ScaffoldResources.ScaffoldDidNotStart);
                        return false;
                    }
                    
                    if (this.Wait)
                    {
                        // We pass the wait flag into the remote process so that it will display the wait message.
                        // It will wait on VS to exit, and we will wait on it.
                        process.WaitForExit();
                    }
                    else
                    {
                        // Wait for Scaffold to reach blocking point then continue
                        WaitHandle[] waitHandles = new WaitHandle[] 
                        { 
                            new ProcessWaitHandle(process),
                            remoteOutputter.BlockingEvent
                        };
                        
                        int n = WaitHandle.WaitAny(waitHandles);
                        
                        if (n == 0)
                            // The process exited unexpectedly; leave the scene of the crime
                            return false;
                    }
                    
                    process.Close();
                }
                catch (Win32Exception e)
                {
                    Output.Error(ScaffoldResources.UnableToStartScaffold(e.Message));
                    return false;
                }
            }
            
            return true;
        }

        private List<string> GrabSystemRspFileReferences(ParsedPath rspFile, RuntimeInfo runtimeInfo)
        {
            string text;
            StringBuilder sb = new StringBuilder();

            using (StreamReader reader = new StreamReader(rspFile))
            {
                text = reader.ReadToEnd();
            }

            Regex r = new Regex("/r[^:]*:(.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            Match m = r.Match(text);

            List<string> paths = new List<string>();

            while (m.Success)
            {
                string s = m.Groups[1].Value;

                // Chop off trailing '\r'; won't be there if last line of file doesn't end in \r\n
                if (s[s.Length - 1] == '\r')
                    s = s.Substring(0, s.Length - 1);

                // Before we add the reference, let's make sure that we can find it in the reference assembly path
                ParsedPath path = new ParsedPath(s, PathType.File).MakeFullPath(runtimeInfo.FxReferenceAssemblyPath);

                if (File.Exists(path))
                {
                    paths.Add(@"$(FxReferenceAssemblyPath)" + path.FileAndExtension);
                }

                m = m.NextMatch();
            }

            return paths;
        }

        private void GetBoolSetting(ScaffoldSection section, string setting, ref bool val)
        {
            try
            {
                for (int i = 0; i < section.Settings.Count; i++)
                {
                    TypeElement element = section.Settings[i];

                    if (String.Compare(element.Key, setting, true) == 0)
                    {
                        if (!TryParseTrueFalse(element.Value, ref val))
                            Output.Warning(ScaffoldResources.UnableToParseValueInConfig(element.Value, element.Key));

                        return;
                    }
                }
            }
            catch (ConfigurationException e)
            {
                Output.Warning(e.Message);
            }
        }

        private void GetStringSetting(IDictionary dict, string setting, ref string val)
        {
            string temp = (string)dict[setting];

            if (!String.IsNullOrEmpty(temp))
                val = temp;
        }

        private void GetBoolSetting(IDictionary dict, string setting, ref bool val)
        {
            string temp = (string)dict[setting];

            if (!String.IsNullOrEmpty(temp))
            {
                if (!TryParseTrueFalse(temp, ref val))
                    Output.Warning(ScaffoldResources.UnableToParseValueInEnvironment(temp, setting));
            }
        }

        private static bool TryParseTrueFalse(string s, ref bool b)
        {
            if ((String.Compare(s, ScaffoldResources.No, true) == 0 ||
                String.Compare(s, ScaffoldResources.False, true) == 0))
            {
                b = false;
                return true;
            }
            else if ((String.Compare(s, ScaffoldResources.Yes, true) == 0 ||
                String.Compare(s, ScaffoldResources.True, true) == 0))
            {
                b = true;
                return true;
            }

            return false;
        }

        private void RealExecute()
        {
            // At this point, we need a program (the default argument)
            if (this.ScriptPath == null)
            {
                Output.Error(ScaffoldResources.NoSourceSpecified);
                return;
            }

            // Fully qualify the program
            this.ScriptPath = this.ScriptPath.MakeFullPath();

            if (this.ScriptPath.File == String.Empty)
            {
                Output.Error(ScaffoldResources.NoEmptyFilename);
                return;
            }
            else if (this.ScriptPath.HasWildcards)
            {
                Output.Error(ScaffoldResources.NoSourceWildcards);
                return;
            }
            else if (this._Language == ScriptLanguage.Unknown)
            {
                Output.Error(ScaffoldResources.FileNameMustEndIn);
                return;
            }

            // TODO-johnls-12/15/2007: More project types coming soon...?
            if (this._Language != ScriptLanguage.CSharp)
            {
                Output.Error(ScaffoldResources.OnlyCSharpSourcesSupported);
                return;
            }

            // Look for installed VS versions get information about them
            IList<VisualStudioInfo> vsInfos = VisualStudioInfo.GetInstalledVisualStudios();

            if (vsInfos.Count == 0)
            {
                // Must have at least one supported version of Visual Studio installed
                Output.Error(ScaffoldResources.VSNotInstalled(StringUtility.Join(", ", ScriptInfo.ValidVisualStudioVersions)));
                return;
            }

            // Get list of installed runtimes.
            IList<RuntimeInfo> runtimeInfos = RuntimeInfo.GetInstalledRuntimes();

            // If VS is installed we have at least one runtime...
            if (runtimeInfos.Count == 0)
            {
                Output.Error(ScaffoldResources.RuntimeNotInstalled(StringUtility.Join(", ", ScriptInfo.ValidRuntimeVersions)));
                return;
            }

            ScriptInfo scriptInfo = null;
            VisualStudioInfo vsInfo;
            RuntimeInfo runtimeInfo;

            if (File.Exists(this.ScriptPath))
            {
                try
                {
                    // Grab script information from script
                    scriptInfo = ScriptInfo.GetScriptInfo(this.ScriptPath);
                }
                catch (ScriptInfoException e)
                {
                    Output.Error(e.Message);
                    return;
                }

                // Validate that VS/CLR/FX versions installed
                runtimeInfo = ((List<RuntimeInfo>)runtimeInfos).Find(
                    v => (scriptInfo.ClrVersion == v.ClrVersion && scriptInfo.FxVersion == v.FxVersion));
                vsInfo = ((List<VisualStudioInfo>)vsInfos).Find(
                    v => (scriptInfo.VsVersion == v.VsVersion));

                if (runtimeInfo == null || vsInfo == null)
                {
                    Output.Error(ScaffoldResources.ScriptsRequiredClrFxAndVsNotInstalled(scriptInfo.ClrVersion, scriptInfo.FxVersion, scriptInfo.VsVersion));
                    return;
                }
            }
            else
            {
                vsInfo = vsInfos[0];
                runtimeInfo = runtimeInfos[0];

                scriptInfo = new ScriptInfo(vsInfo.VsVersion, runtimeInfo.ClrVersion, runtimeInfo.FxVersion, new List<string>());

                // Go grab references from the csc.rsp file and add CodeRunner.dll and ToolBelt.dll 
                // in the same directory as Scaffold.exe.
                scriptInfo.References.AddRange(GrabSystemRspFileReferences(runtimeInfo.FxInstallPath.Append("csc.rsp", PathType.File), runtimeInfo));
                scriptInfo.References.Add(@"$(CodeRunnerPath)CodeRunner.dll");
                scriptInfo.References.Add(@"$(CodeRunnerPath)ToolBelt.dll");
            }
			
            ParsedPath projectDir = CreateTempDirectory();

            if (!CreateProjectFiles(scriptInfo, vsInfo, runtimeInfo, projectDir))
                return;

            StartDevenvAndWait(vsInfos[0], new ParsedPath(
                projectDir.VolumeAndDirectory + this.ScriptPath.File + ".sln", PathType.File));

            DeleteDirectory(projectDir);
        }

        public static bool HaveRequiredVisuaStudio(ScriptInfo scriptInfo, IList<VisualStudioInfo> vsInfos)
        {
            foreach (var vsInfo in vsInfos)
            {
                if (scriptInfo.VsVersion == vsInfo.VsVersion)
                {
                    return true;
                }
            }

            return false;
        }
 
		private ParsedPath CreateTempDirectory()
		{
			// Create a temporary directory in which to put our scaffold project
			ParsedPath projectDir;
			Random rand = new Random();

			do
			{
				projectDir = new ParsedPath(String.Format("{0}Scaffold_{1:X8}", this.ScriptPath.VolumeAndDirectory, rand.Next()), PathType.Directory);
			}
			while (Directory.Exists(projectDir));

			Directory.CreateDirectory(projectDir);
			return projectDir;
		}

        #endregion

        #region Task Overrides
        /// <summary>
        /// Execute the task
        /// </summary>
        /// <returns>true if task succeeds, false otherwise.</returns>
        public void Execute()
        {
            if (!runningFromCommandLine)
            {
                string arguments = Parser.Arguments;
                Output.Message(MessageImportance.Low, Parser.CommandName + Parser.Arguments);
            }

            // If the help option appears anywhere, just show the help
            if (this.ShowHelp)
            {
                Output.Message(Parser.LogoBanner);
                Output.Message(Parser.Usage);
                Output.Message("");
                return;
            }

            if (NoRemoting)
            {
                RealExecute();
            }
            else if (RemotingUrl == null) 
            {
                // If we are not the second instance of Scaffold, then start it and return
                StartScaffoldAndRemote();
            }
            else
            {
                // Otherwise swap in the remote outputter and continue
                using (RemoteOutputter remoteOutputter = new RemoteOutputter(RemotingUrl))
                { 
                    this.Output.Outputter = remoteOutputter;
                    
                    RealExecute();
                }
            }
        }

        #endregion
        
        #region IProcessConfiguration Members

        /// <summary>
        /// Process th e
        /// </summary>
        /// <param name="userLevel"></param>
        /// <returns></returns>
        bool IProcessConfiguration.ProcessConfiguration(System.Configuration.ConfigurationUserLevel userLevel)
        {
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(userLevel);

                if (!config.HasFile)
                    return true;

                ConfigurationSection tempSection = null;
                ScaffoldSection section = null;

                try
                {
                    tempSection = config.GetSection("scaffold");
                    section = tempSection as ScaffoldSection;
                }
                catch (ConfigurationException e)
                {
                    Output.Error(ScaffoldResources.ProblemLoadingExeConfiguration(e.Message));
                }

                if (section == null)
                {
                    if (tempSection != null)
                    {
                        Output.Warning(ScaffoldResources.ScaffoldSectionPresentButWrongType);
                    }
                    
                    // Otherwise the section just isn't there
                    return true;
                }

                GetBoolSetting(section, "Verbose", ref verbose);
                GetBoolSetting(section, "NoRemoting", ref noRemoting);
            }
            catch (ConfigurationErrorsException)
            {
                Output.Warning(ScaffoldResources.ApplicationConfigurationCouldNotBeLoaded);
                return false;
            }

            return true;
        }

        #endregion

        #region IProcessCommandLine Members

        bool IProcessCommandLine.ProcessCommandLine(string[] args)
        {
            runningFromCommandLine = true;

            CommandLineParser parser = new CommandLineParser(typeof(ScaffoldTool), typeof(ScaffoldResources));

            try
            {
                Parser.ParseAndSetTarget(args, this);
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
            string allVars = Environment.GetEnvironmentVariable(scaffoldEnvironmentVar);

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

            GetStringSetting(environ, "RemotingUrl", ref remotingUrl);
            GetBoolSetting(environ, "NoRemoting", ref noRemoting);

            return true;
        }

        #endregion
    }
}
