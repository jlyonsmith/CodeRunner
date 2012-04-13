using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
#if USE_TOASTER
using Toaster;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
using CodeRunner;
using ToolBelt;
using EnvDTE;
using EnvDTE80;

namespace CodeRunner.UnitTest
{
	[TestClass]
	public class ScaffoldTests
	{
		private static TestContext context;
		
		public ScaffoldTests()
		{
		}

		[ClassInitialize()]
		public static void ClassInit(TestContext context)
		{
			ScaffoldTests.context = context;
		}

		// TODO: Test that Scaffold starts without error when there is no .config
		[TestMethod]
		public void TestNoConfig()
		{
		}

		// TODO: Test that Scaffold starts without error when there is a .config with no appsettings section
		[TestMethod]
		public void TestEmptyConfig()
		{
		}
		
        [DeploymentItem(@"UnitTest\TestSource\HelloWorld.csr")]
        [TestMethod]
        public void TestScaffoldClass()
        {
            ScaffoldTool scaffold = new ScaffoldTool(new TraceOutputter());

            // TODO-johnls-1/2/2008: This property is really just a hack
            scaffold.NoRemoting = true;
            scaffold.ScriptPath = new ParsedPath("HelloWorld.csr", PathType.File);

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                EnvDTE._DTE dte = null;
                
                while ((dte = GetIDEInstances("HelloWorld.sln")) == null)
                    System.Threading.Thread.Sleep(100);
                    
                dte.Application.Quit();
            });

            t.Start();

            scaffold.Execute();
            
            if (scaffold.Output.HasOutputErrors)
            {
                t.Abort();
            }
            else
            {    
                if (!t.Join(10000))
                    t.Abort();
            }
            
            Assert.IsFalse(scaffold.Output.HasOutputErrors, "Scaffold failed");
        }
		
		[TestMethod]
		public void TestHelpSwitch()
		{
            string output1;

            ToolBelt.Command.Run("scaffold.exe /?", out output1);

            Assert.IsTrue(Regex.IsMatch(output1,
                @"^.*Scaffolding.*\. (Debug|Release) Version \d+\.\d+\.\d+\.\d+\r\nCopyright \(c\).*\r\n\r\nSyntax:\s+scaffold \[switches\] <script-name>\r\n\r\nDescription:\s+.+\r\n(\s{10,}.+\r\n)+\r\nSwitches:\r\n((  /.+\r\n)|(\r\n)|(\s{10}.+\r\n))+",
                RegexOptions.Multiline | RegexOptions.ExplicitCapture));

            string output2;

            ToolBelt.Command.Run("scaffold /HELP", out output2);

            Assert.AreEqual(output1, output2);

            ToolBelt.Command.Run("scaffold /HELP blah", out output2);

            Assert.AreEqual(output1, output2);
        }
		
		// Test missing default argument
		[TestMethod]
		public void TestMissingDefaultArgument()
		{
            string output;

            int n = ToolBelt.Command.Run("scaffold.exe", out output);

            Assert.IsTrue(Regex.IsMatch(output, @"^error: .*\r\n", RegexOptions.Multiline));
        }

        #region Private Methods

        private class NativeMethods
        {
            [DllImport("ole32.dll")]
            public static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

            [DllImport("ole32.dll")]
            public static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
        }

        // Get a table of the currently running instances of the Visual Studio .NET IDE.
        private static EnvDTE._DTE GetIDEInstances(string name)
        {
            Hashtable runningObjects = GetRunningObjectTableSnapshot();
            IDictionaryEnumerator rotEnumerator = runningObjects.GetEnumerator();

            while (rotEnumerator.MoveNext())
            {
                string candidateName = (string)rotEnumerator.Key;
                
                // VS DTE objects always start with this magic prefix...
                if (!candidateName.StartsWith("!VisualStudio.DTE"))
                    continue;

                EnvDTE._DTE ide = rotEnumerator.Value as EnvDTE._DTE;

                if (ide == null)
                    continue;

                try
                {
                    string solutionFile = ide.Solution.FullName;
                
                    if (solutionFile != String.Empty && solutionFile.IndexOf(name) >= 0)
                    {
                        return ide;
                    }
                }
                catch
                {
                }
            }
            
            return null;
        }
        private static Hashtable GetRunningObjectTableSnapshot()
        {
            Hashtable result = new Hashtable();
            IRunningObjectTable runningObjectTable;
            IEnumMoniker monikerEnumerator;
            IMoniker[] monikers = new IMoniker[1];

            NativeMethods.GetRunningObjectTable(0, out runningObjectTable);
            runningObjectTable.EnumRunning(out monikerEnumerator);
            monikerEnumerator.Reset();

            while (monikerEnumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                object runningObjectVal = null;

                try
                {
                    runningObjectTable.GetObject(monikers[0], out runningObjectVal);
                }
                catch (FileNotFoundException)
                {
                    // This happens if the VS instance isn't properly initialized
                    continue;
                }

                if (runningObjectVal == null)
                    continue;

                IBindCtx ctx;

                NativeMethods.CreateBindCtx(0, out ctx);

                string runningObjectName;
                monikers[0].GetDisplayName(ctx, null, out runningObjectName);

                result[runningObjectName] = runningObjectVal;
            }

            return result;
        }

        #endregion        
	}
}
