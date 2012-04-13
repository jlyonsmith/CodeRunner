using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace CodeRunner
{
    /// <summary>
    /// A class to allow Process objects to be used in WaitHandle operations.
    /// </summary>
    public class ProcessWaitHandle : WaitHandle
    {
        private Process process;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pid"></param>
        public ProcessWaitHandle(int pid) : this(Process.GetProcessById(pid)) 
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="process"></param>
        public ProcessWaitHandle(Process process)
        {
            if (process == null)
                throw new ArgumentNullException("process");

            this.SafeWaitHandle = new SafeWaitHandle(process.Handle, false);

            // Keep a reference to the process so that the handle is not GC'd.  This won't stop
            // the process object from being explicitly Closed/Disposed though which will hork 
            // the handle.
            this.process = process;
        }

        /// <summary>
        /// Dispose of the handle
        /// </summary>
        /// <param name="explicitDisposing"></param>
        protected override void Dispose(bool explicitDisposing)
        {
            base.Dispose(explicitDisposing);

            if (explicitDisposing)
            {
               process = null;
            }
        }
    }
}