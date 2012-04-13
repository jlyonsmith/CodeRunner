using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Security;
using System.IO;
using Microsoft.Win32;

namespace CodeRunner
{
    public class VisualStudioInfo
    {
        private VisualStudioInfo(string devEnvExe)
        {
            this.devEnvExe = devEnvExe;
            this.version = FileVersionInfo.GetVersionInfo(devEnvExe);
        }

        private FileVersionInfo version;
        private string devEnvExe;

        public string VsBuild
        {
            get
            {
                return version.ProductBuildPart.ToString();
            }
        }

        public string VsVersion
        {
            get
            {
                return version.ProductMajorPart + "." + version.ProductMinorPart;
            }
        }

        public string DevEnvExe
        {
            get
            {
                return devEnvExe;
            }
        }

        public static IList<VisualStudioInfo> GetInstalledVisualStudios()
		{
			string[] vsRegistryRoots = 
			{
				@"SOFTWARE\Microsoft\VisualStudio\10.0",
				@"SOFTWARE\Wow6432Node\Microsoft\VisualStudio\10.0",
				@"SOFTWARE\Microsoft\VisualStudio\9.0",
				@"SOFTWARE\Wow6432Node\Microsoft\VisualStudio\9.0",
				@"SOFTWARE\Microsoft\VisualStudio\8.0",
				@"SOFTWARE\Wow6432Node\Microsoft\VisualStudio\8.0",
			};
			
			RegistryKey rk = null;
            List<string> exeNames = new List<string>();
			
			for (int i = 0; i < vsRegistryRoots.Length; ++i)
			{
                string exeName = null;
                rk = Registry.LocalMachine.OpenSubKey(vsRegistryRoots[i], false);
				
				if (rk != null)
				{
					// We have a key, but do we have the Installdir value?  If we are running on x64, then 
					// the actual 32-bit VS registry keys will be under Wow6432Node.
					try
					{
						object obj = rk.GetValue("InstallDir");

						if (obj != null)
						{
							exeName = obj.ToString();
							exeName += (exeName.EndsWith(@"\") ? "" : @"\") + "devenv.exe";

                            if (!exeNames.Contains(exeName) && File.Exists(exeName))
                            {
                                exeNames.Add(exeName);
                            }
						}
					}
					catch (Exception e)
					{
                        // Eat security and not found exceptions
                        if (!(e is SecurityException ||
                            e is UnauthorizedAccessException ||
                            e is IOException))
                        {
                            throw;
                        }
					}
				}
			}

            List<VisualStudioInfo> vsInfos = new List<VisualStudioInfo>();

            foreach (string exeName in exeNames)
                vsInfos.Add(new VisualStudioInfo(exeName));

			// This list could be empty
            return vsInfos;
		}
    }
}
