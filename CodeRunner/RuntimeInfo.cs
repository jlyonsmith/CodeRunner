using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using ToolBelt;
using System.IO;
using Microsoft.Win32;

namespace CodeRunner
{
    public class RuntimeInfo
    {
        public RuntimeInfo(
            string clrVersion,
            ParsedPath clrInstallPath,
            string fxVersion,
            ParsedPath fxInstallPath,
            ParsedPath fxRefAsmPath)
        {
            this.ClrVersion = clrVersion;
            this.ClrInstallPath = clrInstallPath;
            this.FxVersion = fxVersion;
            this.FxInstallPath = fxInstallPath;
            this.FxReferenceAssemblyPath = fxRefAsmPath;
        }
        
        /// <summary>
        /// Gets the CLR runtime major.minor version, either 1.0, 1.1, 2.0, 4.0.
        /// </summary>
        public string ClrVersion { internal set; get; }

        /// <summary>
        /// Gets the CLR install path
        /// </summary>
        public ParsedPath ClrInstallPath { internal set; get; }

        /// <summary>
        /// Gets the version of .NET Framework, either 2.0, 3.0, 3.5 or 4.0.  
        /// </summary>
        public string FxVersion { internal set; get; }

        /// <summary>
        /// Gets .NET Framework install path.
        /// </summary>
        public ParsedPath FxInstallPath { internal set; get; }

        /// <summary>
        /// Gets a value containing the path to the .NET Framework reference assemblies or an empty path if not installed.
        /// </summary>
        public ParsedPath FxReferenceAssemblyPath { internal set; get; }

        public static IList<RuntimeInfo> GetInstalledRuntimes()
        {
            List<RuntimeInfo> runtimeInfos = new List<RuntimeInfo>();
            string clrVersion;
            ParsedPath clrInstallPath;
            string fxVersion;
            ParsedPath fxInstallPath;
            ParsedPath fxRefAsmPath;

            // First look for CLR 2.0 and .NET 2.0, 3.0 and 3.5
            ParsedPath windir = new ParsedPath(Environment.GetEnvironmentVariable("windir"), PathType.Directory);
            ParsedPath programFiles = new ParsedPath(Environment.GetEnvironmentVariable("ProgramW6432"), PathType.Directory);
            ParsedPath frameworkDir = new ParsedPath(String.Format(@"Microsoft.NET\Framework{0}\", 
                ProcessUtility.IsThis64BitProcess ? "64" : ""), PathType.Directory);
            ParsedPath mscorlib;

            mscorlib = windir.Append(frameworkDir).Append(@"v4.0.30319\mscorlib.dll", PathType.File);

            if (File.Exists(mscorlib))
            {
                clrVersion = "4.0";
                clrInstallPath = mscorlib.VolumeAndDirectory;

                fxVersion = clrVersion;
                fxInstallPath = clrInstallPath;
                fxRefAsmPath = clrInstallPath;
                runtimeInfos.Add(new RuntimeInfo(clrVersion, clrInstallPath, fxVersion, fxInstallPath, fxRefAsmPath));
            }

            mscorlib = windir.Append(frameworkDir).Append(@"v2.0.50727\mscorlib.dll", PathType.File);

            if (File.Exists(mscorlib))
            {
                clrVersion = "2.0";
                clrInstallPath = mscorlib.VolumeAndDirectory;

                // First CLR/Fx is easy...
                fxVersion = clrVersion;
                fxInstallPath = clrInstallPath;
                fxRefAsmPath = clrInstallPath;
                runtimeInfos.Add(new RuntimeInfo(clrVersion, clrInstallPath, fxVersion, fxInstallPath, fxRefAsmPath));

                // Now look for other Fx's
                foreach (string version in new string[] { "3.5", "3.0" })
                {
                    fxVersion = version;
                    
                    ParsedPath csc = windir.Append(frameworkDir).Append(String.Format(@"v{0}\csc.exe", fxVersion), PathType.File);

                    if (File.Exists(csc))
                    {
                        fxInstallPath = csc.VolumeAndDirectory;
                        fxRefAsmPath = programFiles.Append(String.Format(@"Reference Assemblies\Microsoft\Framework\v{0}", fxVersion), PathType.Directory);

                        if (Directory.Exists(fxRefAsmPath))
                        {
                            runtimeInfos.Add(new RuntimeInfo(clrVersion, clrInstallPath, fxVersion, fxInstallPath, fxRefAsmPath));
                        }
                    }
                }
            }

            // This list could be empty
            return runtimeInfos;
        }
    }
}
