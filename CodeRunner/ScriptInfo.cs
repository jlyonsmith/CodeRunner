using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using ToolBelt;
using System.Diagnostics;

namespace CodeRunner
{
    public class ScriptInfo
    {
        public ScriptInfo(string vsVersion, string clrVersion, string fxVersion, List<string> references)
        {
            this.VsVersion = vsVersion;
            this.FxVersion = fxVersion;
            this.ClrVersion = clrVersion;
            this.References = references;
        }

        public string VsVersion { internal set; get; }
        public string FxVersion { internal set; get; }
        public string ClrVersion { internal set; get; }
        public List<string> References { internal set; get; }

        public static readonly string[][] ValidClrFxVsVersions =
        {
            new string[] { "2.0", "2.0", "8.0" },
            new string[] { "2.0", "3.0", "8.0" },
            new string[] { "2.0", "3.5", "8.0" },
            new string[] { "2.0", "2.0", "9.0" },
            new string[] { "2.0", "3.0", "9.0" },
            new string[] { "2.0", "3.5", "9.0" },
            new string[] { "2.0", "2.0", "10.0" },
            new string[] { "2.0", "3.0", "10.0" },
            new string[] { "2.0", "3.5", "10.0" },
            new string[] { "4.0", "4.0", "10.0" }
        };

        public static readonly string[] ValidRuntimeVersions =
        {
            "2.0", "4.0"
        };
        public static readonly string[] ValidFrameworkVersions = 
        {
            "2.0", "3.0", "3.5", "4.0"
        };
        public static readonly string[] ValidVisualStudioVersions = 
        {
            "8.0", "9.0", "10.0"
        };

        #region Static Methods
		public static ScriptInfo GetScriptInfo(ParsedPath scriptPath)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException();

            StringBuilder sb = new StringBuilder();

            using (StreamReader reader = new StreamReader(scriptPath))
            {
                string line = reader.ReadLine();

                // Read until there is a non-blank line
                while (line != null)
                {
                    line = line.Trim();

                    if (line.Length != 0)
                        break;

                    line = reader.ReadLine();
                }

                while (line != null && line.StartsWith("///"))
                {
                    sb.Append(line.Substring(3).Trim());
                    line = reader.ReadLine();
                }
            }

            if (sb.Length == 0)
            {
                throw new ScriptInfoException(CodeRunnerResources.ScriptHeaderNotFound);
            }

            string vsVersion = "0.0";
            string fxVersion = "0.0";
            string clrVersion = "0.0";
            List<string> references = new List<string>();

            // If we have some lines, try and interpret them as an XML snippet with minimal error checking
            using (StringReader stringReader = new StringReader(sb.ToString()))
            {
                NameTable nameTable = new NameTable();

                string localVs = nameTable.Add("vs");
                string localFx = nameTable.Add("fx");
                string localClr = nameTable.Add("clr");
                string localRef = nameTable.Add("ref");

                XmlReaderSettings settings = new XmlReaderSettings();

                settings.IgnoreComments = true;
                settings.IgnoreWhitespace = true;
                settings.IgnoreProcessingInstructions = true;
                settings.ValidationType = ValidationType.None;
                settings.NameTable = nameTable;

                try
                {
                    XmlReader xmlReader = XmlReader.Create(stringReader);

                    xmlReader.Read();

                    while (!xmlReader.EOF)
                    {
                        string local = xmlReader.LocalName;

                        if (xmlReader.IsStartElement() &&
                            (local == localClr || local == localFx || local == localRef || local == localVs))
                        {
                            xmlReader.MoveToContent();
                            string content = xmlReader.ReadString();

                            if (local == localFx)
                            {
                                fxVersion = content;
                            }
                            else if (local == localClr)
                            {
                                clrVersion = content;
                            }
                            else if (local == localVs)
                            {
                                vsVersion = content;
                            }
                            else if (local == localRef)
                            {
                                references.Add(content);
                            }

                            xmlReader.ReadEndElement();
                        }
                        else
                            xmlReader.Read();
                    }
                }
                catch (XmlException e)
                {
                    throw new ScriptInfoException(e.Message);
                }
            }

            if (!ValidRuntimeVersions.Any(s => (s == clrVersion)))
            {
                throw new ScriptInfoException(CodeRunnerResources.UnsupportedOrUnknownRuntime(clrVersion, 
                    StringUtility.Join(", ", ValidRuntimeVersions)));
            }

            if (!ValidFrameworkVersions.Any(s => (s == fxVersion)))
            {
                throw new ScriptInfoException(CodeRunnerResources.UnsupportedOrUnknownFx(fxVersion, 
                    StringUtility.Join(", ", ValidFrameworkVersions)));
            }

            if (!ValidRuntimeVersions.Any(s => (s == clrVersion)))
            {
                throw new ScriptInfoException(CodeRunnerResources.UnsupportedOrUnknownVisualStudio(vsVersion, 
                    StringUtility.Join(", ", ValidVisualStudioVersions)));
            }

            if (!ValidClrFxVsVersions.Any(s => (s[0] == clrVersion && s[1] == fxVersion && s[2] == vsVersion)))
            {
                throw new ScriptInfoException(CodeRunnerResources.UnsupportedClrFxVsCombination(clrVersion, fxVersion, vsVersion));
            }

            return new ScriptInfo(vsVersion, clrVersion, fxVersion, references);
        }

        internal static IList<ParsedPath> GetFullScriptReferencesPaths(ParsedPath scriptPath, ScriptInfo scriptInfo, RuntimeInfo runtimeInfo)
        {
            List<ParsedPath> paths = new List<ParsedPath>();
            Dictionary<string, string> dict = new Dictionary<string, string>();

            dict.Add("FxInstallPath", runtimeInfo.FxInstallPath);
            dict.Add("FxReferenceAssemblyPath", runtimeInfo.FxReferenceAssemblyPath);
            dict.Add("ClrInstallPath", runtimeInfo.ClrInstallPath);
            dict.Add("ScriptPath", scriptPath.VolumeAndDirectory);
            dict.Add("CodeRunnerPath", new ParsedPath(Process.GetCurrentProcess().MainModule.FileName, PathType.File).VolumeAndDirectory);

            string reference = String.Empty;

            try
            {
                for (int i = 0; i < scriptInfo.References.Count; i++)
                {
                    reference = scriptInfo.References[i];

                    string fullReference = StringUtility.ReplaceTags(reference, "$(", ")", dict);
                    ParsedPath path = new ParsedPath(fullReference, PathType.File).MakeFullPath(scriptPath);

                    paths.Add(path);
                }
            }
            catch (ArgumentException e)
            {
                // Could be bad crap in the reference paths
                throw new ScriptInfoException(CodeRunnerResources.BadReferencePaths(reference, e.Message));
            }

            return paths;
        }

	    #endregion    
    }
}
