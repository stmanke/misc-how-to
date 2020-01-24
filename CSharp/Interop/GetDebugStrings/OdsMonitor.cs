using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GetDebugStrings
{
    // namespace wide delegate used when firing OdsMonitor.OnOuputDebugString event, implementation in client using OdsMonitor class
    public delegate void OnOutputDebugStringHandler(int pid, string text);

    public sealed partial class OdsMonitor
    {
        // control entry/exit from critical sections
        private static readonly Object thisLock = new Object();

        // track the thread responsible for capturing/processing debug strings
        private static Thread odsCapturer;

        // track current process id -- only want to pass through debug messages that didn't originate from current process
        private static int currentPid;

        public static event OnOutputDebugStringHandler OnOutputDebugString;

        // TODO: less quirky name
        // ensure singleton status on machine
        private static Mutex highlander;

        // event handle for DBWIN_BUFFER_READY
        private static IntPtr bufferReadyEvent;

        // event handle for DBWIN_DATA_READY
        private static IntPtr dataReadyEvent;

        // handle for shared memory
        private static IntPtr sharedMemory;

        // handle for view into shared memory
        private static IntPtr memoryView;

        // make sure compiler doesn't auto-generate constructor!
        private OdsMonitor()
        {
        }

        public static void Start()
        {
            // make sure the OdsMonitor can't be trying to start and stop capturing at the same time
            lock (thisLock)
            {
                // don't try to start ODS monitoring if it's already running
                if (odsCapturer == null)
                {
                    // out of paranoia...
                    if (!Environment.OSVersion.ToString().Contains("Windows"))
                    {
                        throw new NotSupportedException("ODS Monitoring only available on Microsoft operating systems.");
                    }

                    // use mutex to verify this instance is the only one currently running, if multiple instances (or
                    // different implementations using this class) are running, there's no way to tell which instance will
                    // pick up a debug message
                    bool newMutex;
                    highlander = new Mutex(false, typeof(OdsMonitor).Namespace, out newMutex);
                    if (!newMutex)
                    {
                        throw new InvalidOperationException("ODS Monitoring already running in another instance or implementation.");
                    }

                    InitializeOdsCapture();
                }
            }
        }

        private static void InitializeOdsCapture()
        {
            var sd = new SECURITY_DESCRIPTOR();

            // initialize to absolute format, important when setting dacl later...
            // ref: https://msdn.microsoft.com/en-us/library/windows/desktop/aa378863(v=vs.85).aspx
            if (!InitializeSecurityDescriptor(ref sd, SECURITY_DESCRIPTOR_REVISION))
            {
                throw new OdsMonitorException("Failed to initialize security descriptor, could not start ODS Monitoring");
            }

            // the sd has dacl section, assign a null dacl (no protection for object), don't use default dacl
            // ref: https://msdn.microsoft.com/en-us/library/windows/desktop/aa379583(v=vs.85).aspx
            if (!SetSecurityDescriptorDacl(ref sd, true, IntPtr.Zero, false))
            {
                throw new OdsMonitorException("Failed to set security descriptor DACL, could not start ODS Monitoring");
            }

            var sa = new SECURITY_ATTRIBUTES();

            // gives a security attribute to update, create auto-reset event object, do not signal initial state
            // ref: https://msdn.microsoft.com/en-us/library/windows/desktop/ms682396(v=vs.85).aspx
            // TODO: verify this is enough to access global win32 debug output (i.e., services running as LOCALSYSTEM)
            bufferReadyEvent = CreateEvent(ref sa, false, false, "DBWIN_BUFFER_READY");
            if (bufferReadyEvent == IntPtr.Zero)
            {
                throw new OdsMonitorException("Failed to create event DBWIN_BUFFER_READY, could not start ODS Monitoring");
            }

            dataReadyEvent = CreateEvent(ref sa, false, false, "DBWIN_DATA_READY");
            if (dataReadyEvent == IntPtr.Zero)
            {
                throw new OdsMonitorException("Failed to create event DBWIN_DATA_READY, could not start ODS Monitoring");
            }

            // get a handle to readable shared memory that holds output debug strings
            // invalid pointer means "file" is held in paging file rather than on disk
            // ref: https://msdn.microsoft.com/en-us/library/windows/desktop/aa366537(v=vs.85).aspx
            sharedMemory = CreateFileMapping(new IntPtr(-1), ref sa, PageProtection.ReadWrite, 0, 4096, "DBWIN_BUFFER");
            if (sharedMemory == IntPtr.Zero)
            {
                throw new OdsMonitorException("Failed to create mapping to shared memory 'DBWIN_BUFFER', could not start ODS Monitoring");
            }

            // create a view of the shared memory so we can get the debug strings
            // TODO: last value is # bytes to map at any one time, test to see if that needs to be increased
            memoryView = MapViewOfFile(sharedMemory, SECTION_MAP_READ, 0, 0, 512);
            if (memoryView == IntPtr.Zero)
            {
                throw new OdsMonitorException("Failed to create a mapping view of shared memory, could not start ODS Monitoring");
            }

            currentPid = Process.GetCurrentProcess().Id;
            odsCapturer = new Thread(new ThreadStart(Capture));
            odsCapturer.Start();
        }

        private static void Capture()
        {
            try
            {
                // first thing in the debug strings is a DWORD representing process id, so set our "string pointer" to
                // the memory space after the DWORD
                var stringPortion = new IntPtr(memoryView.ToInt32() + Marshal.SizeOf(typeof(int)));

                // while we haven't been told to stop...
                while (true)
                {
                    // we're listening for a debug string...
                    SetEvent(bufferReadyEvent);
                    var waitResult = WaitForSingleObject(dataReadyEvent, INFINITE);

                    // but! we've been told to stop! drop into finally clause and dispose of everything
                    if (odsCapturer == null)
                    {
                        break;
                    }

                    // did our wait result in a debug string to process? if not, go through loop again
                    if (waitResult == WAIT_OBJECT_0)
                    {
                        // debug string is pid then text -- get the pid, then everything else after the pid "segment"
                        FireOnOutputDebugString(Marshal.ReadInt32(memoryView), Marshal.PtrToStringAnsi(stringPortion));
                    }
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                Dispose();
            }
        }

        private static void FireOnOutputDebugString(int pid, string text)
        {
            // only re-emit if the pid is different from this process id
            if (pid != currentPid)
            {
                Debug.WriteLine($"[{pid}] {text}");

                // TODO: verify this is fast enough in real world, right now only tracking test app that
                // occassionally "mutters" debug string output
                var processes = Process.GetProcesses();
                var pidsToMonitor = processes.Where(x => x.ProcessName.ToLower() == "muttercpp").Select(y => y.Id);

                // a subscriber AND we care? send it on! note: .Contains can be used on empty list, will return false
                if (pidsToMonitor.Contains(pid))
                {
                    OnOutputDebugString?.Invoke(pid, text);
                }
            }
        }

        // safely close all events and resources
        private static void Dispose()
        {
            if (bufferReadyEvent != IntPtr.Zero)
            {
                if (!CloseHandle(bufferReadyEvent)) { throw new OdsMonitorException("Failed to close handle for BufferReadyEvent"); }
                bufferReadyEvent = IntPtr.Zero;
            }

            if (dataReadyEvent != IntPtr.Zero)
            {
                if (!CloseHandle(dataReadyEvent)) { throw new OdsMonitorException("Failed to close handle for DataReadyEvent"); }
                dataReadyEvent = IntPtr.Zero;
            }

            if (memoryView != IntPtr.Zero)
            {
                if (!UnmapViewOfFile(memoryView)) { throw new OdsMonitorException("Failed to unmap view of shared memory"); }
                memoryView = IntPtr.Zero;
            }

            if (sharedMemory != IntPtr.Zero)
            {
                if (!CloseHandle(sharedMemory)) { throw new OdsMonitorException("Failed to close handle to shared memory space"); }
                sharedMemory = IntPtr.Zero;
            }

            if (highlander != null)
            {
                highlander.Close();
                highlander = null;
            }
        }

        public static void Stop()
        {
            // make sure the OdsMonitor can't be trying to start and stop capturing at the same time
            lock (thisLock)
            {
                if (odsCapturer == null)
                {
                    throw new ObjectDisposedException(nameof(OdsMonitor), "ODS Monitoring is already stopped.");
                }

                // this "throws" the event the capturing thread waits for before doing something
                // since we've already set the capturing thread to null, when the event is processed, it will trigger
                // clean shutdown of monitoring
                odsCapturer = null;
                PulseEvent(dataReadyEvent);

                // block main/executing thread until capturing objects disposed of
                while (memoryView != IntPtr.Zero) {; }
            }
        }
    }
}
