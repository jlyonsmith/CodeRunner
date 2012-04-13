using System;
using System.Text;
using System.Collections.Generic;
#if USE_TOASTER
using Toaster;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
using System.IO;
using System.Xml;
using System.Text.RegularExpressions;
using CodeRunner;
using ToolBelt;

namespace CodeRunner.UnitTest
{
    [TestClass]
#if USE_TOASTER
    [DeploymentItem(@"$(SolutionDir)Csr\bin\$(Configuration)\csr.exe")]
    [DeploymentItem(@"$(SolutionDir)Csr\csr.exe.config")]
#else
    [DeploymentItem(@"Csr\bin\debug\csr.exe")]
    [DeploymentItem(@"Csr\bin\debug\csr.pdb")]
    [DeploymentItem(@"Csr\csr.exe.config")]
#endif
    public class CsrTests 
    {
        private static TestContext context;

        public CsrTests() 
        {
        }

        [ClassInitialize()]
        public static void ClassInitialize(TestContext context)
        {
            CsrTests.context = context;
        }
        
        [TestInitialize()]
        public void TestInitialize()
        {
            // Make .config file writeable
            File.SetAttributes("csr.exe.config", File.GetAttributes("csr.exe.config") & ~FileAttributes.ReadOnly);
        }

        [TestMethod]
        public void TestEmptyCommandLine()
        {
            string output;

            int n = Command.Run("csr.exe", out output);

            Assert.IsTrue(Regex.IsMatch(output, @"^error: .*\r\n", RegexOptions.Multiline));
        }

        [TestMethod]
        public void TestNoConfigFile()
        {
            string output;

            try
            {
                File.Move("csr.exe.config", "__csr.exe.config");

                int n = Command.Run("csr.exe", out output);

                Assert.IsTrue(Regex.IsMatch(output, @"^error: .*\r\n", RegexOptions.Multiline));
            }
            finally
            {
                if (File.Exists("__csr.exe.config"))
                    File.Move("__csr.exe.config", "csr.exe.config");
            }
        }

        [TestMethod]
        public void TestBogusSwitch()
        {
            string output;
        
            Command.Run("csr.exe /bogus", out output);
            
            Assert.IsTrue(Regex.IsMatch(output, @"^error: Unknown or invalid.*\r\n", RegexOptions.Multiline));
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\TestSource\HelloWorld.csr")]
        public void TestHelloWorldWithArgs()
        {
            string output;
            string cmd = "csr.exe helloworld.csr 1 2 3";

            Command.Run(cmd, out output);

            Assert.IsTrue(Regex.IsMatch(output, @"Hello world!\r\n1\r\n2\r\n3\r\n", RegexOptions.Multiline));
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\TestSource\GetScriptName.csr")]
        public void TestGetScriptName()
        {
            string output;
            string cmd = "csr.exe getscriptname";  // Left off the .csr on purpose
            
            Command.Run(cmd, out output);
            
            Assert.IsTrue(Regex.IsMatch(output, @".*getscriptname\.csr.*\r\n", RegexOptions.Multiline | RegexOptions.IgnoreCase));
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\TestSource\Unsafe.csr")]
        public void TestUnsafe()
        {
            // TODO: Make sure that if this test fails, the .config gets put back the way it was

            string output;
            string cmd = "csr.exe unsafe.csr";

            XmlPoke("csr.exe.config", "/configuration/csr/settings/add[@key='AllowUnsafeCode']/@value", "no");

            Command.Run(cmd, out output);

            // Save the result so that we can return the .config file to it's old state
            bool succeeded = Regex.IsMatch(output, @"^.*Unsafe\.csr.* error CS0227.*\r\n", RegexOptions.Multiline);

            XmlPoke("csr.exe.config", "/configuration/csr/settings/add[@key='AllowUnsafeCode']/@value", "yes");

            Assert.IsTrue(succeeded);

            Command.Run(cmd, out output);

            Assert.IsTrue(Regex.IsMatch(output, @"Success\r\n", RegexOptions.Multiline));
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\TestSource\HelloWorld.csr")]
        public void TestEnvironmentConfig()
        {
            string output;
            string error;
            string cmd = "csr.exe helloworld.csr";

            Environment.SetEnvironmentVariable("CSR_CONFIG", "DebugMessages=yes");

            Command.Run(cmd, out output, out error);

            Assert.IsTrue(Regex.IsMatch(output, @".*\r\nHello world!\r\n", RegexOptions.Multiline));

            // Explicitly turn off debug messages
            Environment.SetEnvironmentVariable("CSR_CONFIG", "DebugMessages=");
            
            Command.Run(cmd, out output, out error);

            Assert.IsTrue(Regex.IsMatch(output, @"Hello world!\r\n", RegexOptions.Multiline));
            Assert.IsTrue(error.Length == 0);
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\FunkyType\1.0\FunkyType.dll", "FunkyType\\1.0")]
        [DeploymentItem(@"UnitTest\FunkyType\2.0\FunkyType.dll", "FunkyType\\2.0")]
        [DeploymentItem(@"UnitTest\TestSource\UseFunkyType.csr", "FunkyType")]
        [DeploymentItem(@"UnitTest\TestSource\UseTwoFunkyType.csr", "FunkyType")]
        public void TestConflictingAssemblyVersions()
        {
            string output;
            
            Command.Run(@"csr.exe FunkyType\UseFunkyType.csr", out output);
            Assert.AreEqual("1\r\n", output);

            // This will FAIL because FunkyType is defined in two referenced assemblies in the script header
            Command.Run(@"csr.exe FunkyType\UseTwoFunkyType.csr", out output);
            // TODO-johnls-Mar-2011: Add an attribute to allow alias in the script header
            // Assert.IsTrue(Regex.Match(output, @"error CS0433").Success);
        }

        [TestMethod]
        public void TestHelpSwitch()
        {
            string output1;

            Command.Run("csr.exe /?", out output1);

            Assert.IsTrue(Regex.IsMatch(output1,
                @"^C# Code Runner .NET\. (Debug|Release) Version \d+\.\d+\.\d+\.\d+\r\nCopyright \(c\).*\r\n\r\nSyntax:\s+csr \[switches\] <script-name>\[.+?\] \[<arguments> \.\.\.\]\r\n\r\nDescription:\s+.+\r\n(\s{10,}.+\r\n)+\r\nSwitches:\r\n((  /.+\r\n)|(\r\n)|(\s{10}.+\r\n))+",
                RegexOptions.Multiline | RegexOptions.ExplicitCapture));

            string output2;

            Command.Run("csr /HELP", out output2);

            Assert.AreEqual(output1, output2);

            Command.Run("csr /HELP blah", out output2);

            Assert.AreEqual(output1, output2);
        }

        [TestMethod]
        [DeploymentItem(@"UnitTest\TestSource\Throws.csr")]
        public void TestScriptException()
        {
            string output;

            Environment.SetEnvironmentVariable("CSR_CONFIG", "DebugMessages=yes;StackTraces=yes");
                
            // First run is not cached
            Command.Run("csr.exe Throws.csr", out output);
            
            Assert.IsTrue(Regex.IsMatch(output,
                @"(.*?\r\n){8,}error: .*?Object reference not set.*?\r\n^.*?line .*\r\n",
                RegexOptions.Multiline | RegexOptions.ExplicitCapture));
        }

        [DeploymentItem(@"UnitTest\TestSource\HelloWorld.csr")]
        [TestMethod]
        public void TestCsrClassHelloWorld()
        {
            CsrTool csr = new CsrTool(new TraceOutputter());

            csr.ScriptPath = new ParsedPath("HelloWorld.csr", PathType.File);

            csr.Execute();
            
            Assert.IsFalse(csr.Output.HasOutputErrors);
        }

        [DeploymentItem(@"UnitTest\TestSource\WontCompile.csr")]
        [TestMethod]
        public void TestCsrClassWontCompile()
        {
            CsrTool csr = new CsrTool(new TraceOutputter());

            csr.ScriptPath = new ParsedPath("WontCompile.csr", PathType.File);

            csr.Execute();

            Assert.IsFalse(csr.Output.HasOutputErrors);
        }

        [DeploymentItem(@"UnitTest\TestSource\ExtensionMethod.csr")]
        [TestMethod]
        public void TestCsrClassExtensionMethod()
        {
            // Test that .NET C# v3.5 features work
            CsrTool csr = new CsrTool(new TraceOutputter());

            csr.ScriptPath = new ParsedPath("ExtensionMethod.csr", PathType.File);

            csr.Execute();

            Assert.IsFalse(csr.Output.HasOutputErrors);
        }

        [TestCleanup()]
        public void TestCleanup()
        {
            // Remove any environment configuration
            Environment.SetEnvironmentVariable("CSR_CONFIG", null);
        }

        private void XmlPoke(string fileName, string xpath, string nodeValue)
        {
            XmlDocument document = null;

            try 
            {
                document = new XmlDocument();
                document.Load(fileName);

                XmlNodeList nodes = document.SelectNodes(xpath);

                foreach (XmlNode node in nodes)  
                {
                    node.InnerXml = nodeValue;
                }

                document.Save(fileName);
            } 
            catch 
            {
                Assert.Fail(String.Format("Unable to replace XML node '{0}' in '{1}'", nodeValue, xpath));
            }
        }
    }
}