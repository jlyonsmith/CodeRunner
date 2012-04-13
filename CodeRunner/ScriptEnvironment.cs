using System;
using System.Reflection;
using System.Security.Permissions;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.Remoting;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ToolBelt;
using System.Collections.Generic;
using System.Xml;

namespace CodeRunner
{
    /// <summary>
    /// Environment class.
    /// </summary>
    public sealed class ScriptEnvironment : MarshalByRefObject
    {
        #region Private Fields

        private static ParsedPath scriptPath;

        #endregion

        #region Constructors

        private ScriptEnvironment()
        {
        }

        static ScriptEnvironment()
        {
        }

        #endregion

        #region Class Properties

        /// <summary>
        /// Gets the path of the currently running script if the script is being run from the runner,
        /// else the path of for main module of the currently executing process.
        /// </summary>
        /// <exception cref="System.Security.SecurityException">The path can only be set by the CSR utility.</exception>
        public static ParsedPath ScriptPath
        {
            get 
            {
                if (scriptPath == null)
                {
                    // Getting the script path right is a little complicated.  Either (a) the Scaffold or Csr tasks will set the 
                    // name of the ScriptPath directly before running the script or (b) the script is being run from the Scaffold generated
                    // VS solution, in which case we can deduce the script path from a combination of the main module name and the directory one 
                    // level up from the Scaffold temporary directory.  If (a) then we will never most of this code.

                    ParsedPath modulePath = new ParsedPath(Process.GetCurrentProcess().MainModule.FileName.ToLower(), PathType.File);

                    // If running from either of these two processes, they will set the script path via the ScriptPath property
                    if (modulePath.FileAndExtension.CompareTo("scaffold.exe") != 0 &&
                        modulePath.FileAndExtension.CompareTo("csr.exe") != 0)
                    {
                        // So, if this is process is being run from a scaffolded project, the directory ends in something like ...\Scaffold_1234\bin\debug.  
                        // So if we go up 3 directories we should find the script.
                        if (modulePath.DirectoryDepth >= 3)
                        {
                            ParsedPath parentPath = modulePath.MakeParentPath(-3);

                            // Look for a .csr, which is most likely					
                            scriptPath = new ParsedPath(parentPath.VolumeAndDirectory + modulePath.File + ".csr", PathType.File);
                            
                            if (!File.Exists(scriptPath))
                            {
                                // OK, maybe it's a .cs file
                                scriptPath = new ParsedPath(parentPath.VolumeAndDirectory + modulePath.File + ".cs", PathType.File);
                                
                                if (!File.Exists(scriptPath))
                                {
                                    // Uh, dunno what it is.  We could be nuts here and start searching, but is it really worth it?
                                    scriptPath = null;
                                }
                            }
                        }

                        if (scriptPath == null)
                        {
                            // Worst case, we just use the module name with the .csr extension
                            scriptPath = new ParsedPath(modulePath.VolumeDirectoryAndFile + ".csr", PathType.File);
                        }
                    }
                }

                return scriptPath;
            }
            internal set 
            {
                scriptPath = value;
            }
        }

        /// <summary>
        /// Gets the current script header information for the currently running script.
        /// </summary>
        public static ScriptInfo ScriptInfo
        {
            get
            {
                return ScriptInfo.GetScriptInfo(ScriptEnvironment.ScriptPath);
            }
        }

        /// <summary>
        /// Gets the language for the currently running script
        /// </summary>
        public static ScriptLanguage ScriptLanguage
        {
            get
            {
                return GetScriptLanguageFromExtension(ScriptPath.Extension);
            }
        }

        #endregion

        #region Internal

        internal static IList<ParsedPath> FullyQualifiedReferences { get; set; }

        internal static ScriptLanguage GetScriptLanguageFromExtension(string ext)
        {
            switch (ext)
            {
                case ".cs":
                case ".csr":
                    return ScriptLanguage.CSharp;

                default:
                    return ScriptLanguage.Unknown;
            }
        }

        internal static string GetExtensionFromScaffoldLanguage(ScriptLanguage language)
        {
            switch (language)
            {
                case ScriptLanguage.CSharp:
                    return ".csproj";

                case ScriptLanguage.VisualBasic:
                    return ".vbproj";

                default:
                    throw new NotSupportedException();
            }
        }

        #endregion
    }
}
