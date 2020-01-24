using System;
using System.Runtime.InteropServices;

namespace GetDebugStrings
{
    public class OdsMonitorException : Exception
    {
        public OdsMonitorException()
        {
        }

        public OdsMonitorException(string message) : base(AddLastError(message))
        {
        }

        private static string AddLastError(string message)
        {
            return string.Format($"{message}. Last Win32 error: {Marshal.GetLastWin32Error()}");
        }
    }
}
