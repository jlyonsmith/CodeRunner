using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace CodeRunner
{
	/// <summary>
	/// Process utilities
	/// </summary>
	public sealed class ProcessUtility
	{
        #region Private Fields
        private static bool haveParentProcessId = false;
        private static int inheritedFromProcessId;

        #endregion		
		
        #region Private Structures
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct NtProcessBasicInfo
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public UIntPtr UniqueProcessId;
            public UIntPtr InheritedFromUniqueProcessId;
        }

        #endregion

        #region Private Classes
        [SuppressUnmanagedCodeSecurity]
        private class NativeMethods
        {
            [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
            public static extern int NtQueryInformationProcess(
                HandleRef processHandle, int query, ref NtProcessBasicInfo info, int size, out int returnedSize);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool IsWow64Process(HandleRef processHandle, out bool isWow64Process);
        }

        #endregion		
        
        #region Private Methods
        private static void SetParentProcessId(int id)
        {
            inheritedFromProcessId = id;
            haveParentProcessId = true;
        }

		// In the BCL an equivalent method to this appears in the private class System.Diagnostics.NtProcessManager
		private static int GetParentProcessId()
		{
			NtProcessBasicInfo info = new NtProcessBasicInfo();
			Process process = Process.GetCurrentProcess();
			int returnedSize;
			HandleRef href = new HandleRef(process, process.Handle);
			int result = NativeMethods.NtQueryInformationProcess(href, 0, ref info, Marshal.SizeOf(info.GetType()), out returnedSize);

			if (result != 0)
				throw new InvalidOperationException(
					"Unable to retrieve the parent process id", new Win32Exception(result));

			return (int)info.InheritedFromUniqueProcessId;
		}

        #endregion

        #region Public Methods
        public static bool Is32BitOn64BitProcess(int processId)
        {
            Process process = Process.GetProcessById(processId);
            bool isWow64Process = false;
            HandleRef href = new HandleRef(process, process.Handle);

            try
            {
                if (!NativeMethods.IsWow64Process(href, out isWow64Process))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception e)
            {
                if (e is Win32Exception)
                    throw new InvalidOperationException(String.Format("Unable to determine if process id {0} is WOW64", processId), e);
                else
                    throw;
            }

            return isWow64Process;
        }

        #endregion

        #region Public Properties
        /// <summary>
		/// Gets the unique identifier of the process from which this process inherited its environment and handles.
		/// </summary>
		public static int ParentProcessId
		{
			get
			{
				if (!haveParentProcessId)
				{
					SetParentProcessId(GetParentProcessId());
				}

				return inheritedFromProcessId;
			}
		}

		/// <summary>
		/// Indicate if the current process is running under WOW64, i.e. a 32-bit application running on a 64-bit O/S
		/// </summary>
		public static bool IsThisA32BitOn64BitProcess
		{
			get
			{
                return Is32BitOn64BitProcess(Process.GetCurrentProcess().Id);
			}
		}

		/// <summary>
		/// Indicate if the current process is running under WOW64, i.e. a 32-bit application running on a 64-bit O/S
		/// </summary>
		public static bool IsThis64BitProcess
		{
			get
			{
				return Marshal.SizeOf(typeof(IntPtr)) == 8;
			}
		}

    #endregion	
	}
}
