using System;
using System.Text;
using System.Collections.Generic;
#if USE_TOASTER
using Toaster;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
using CodeRunner;

namespace CodeRunner.UnitTest
{
	[TestClass]
	public class ProcessUtilityTests
	{
		public ProcessUtilityTests()
		{
		}

		[TestMethod]
		public void TestParentProcessID()
		{
			// Just test that this doesn't fail
			int id = ProcessUtility.ParentProcessId;
		}
	}
}
