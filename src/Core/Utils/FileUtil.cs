﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
namespace Nekres.Music_Mixer
{
    internal static class FileUtil
    {
        public static bool IsLocalPath(string p)
        {
            return new System.Uri(p).IsFile;
        }

        /// <summary>
        /// Sanitize the filename and replace illegal characters.
        /// </summary>
        /// <param name="fileName">The filename</param>
        /// <param name="replacement">The replacement string for illegal characters.</param>
        /// <returns>The sanitized filename</returns>
        public static string Sanitize(string fileName, string replacement = " ")
        {
            var temp = fileName.Trim().Split(Path.GetInvalidFileNameChars());
            for (var i = 0; i < temp.Length; i++)
                temp[i] = temp[i].Trim();
            return string.Join(replacement, temp);
        }

        public static async Task<bool> DeleteAsync(string filePath)
        {
            return await Task.Run(() => {
                var timeout = DateTime.UtcNow.AddMilliseconds(5000);
                while (DateTime.UtcNow < timeout)
                {
                    try
                    {
                        File.Delete(filePath);
                        return true;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        if (DateTime.UtcNow < timeout) continue;
                        MusicMixer.Logger.Error(e, e.Message);
                        break;
                    }
                }
                return false;
            });
        }

        /// <summary>
        /// Checks if a file is currently locked.
        /// </summary>
        /// <remarks>
        /// Suffers from thread race condition.
        /// </remarks>
        /// <param name="uri">The filename.</param>
        /// <returns><see langword="True"/> if file is locked or does not exist. Otherwise <see langword="false"/>.</returns>
        public static bool IsFileLocked(string uri)
        {
            FileStream stream = null;
            try {
                stream = File.Open(uri, FileMode.Open, FileAccess.Read, FileShare.None); 
                // ERROR_SHARING_VIOLATION
            } catch (IOException e) when ((e.HResult & 0x0000FFFF) == 32) {
                return true;
                // ERROR_LOCK_VIOLATION
            } catch (IOException e) when ((e.HResult & 0x0000FFFF) == 33) {
                return true;
            } finally {
                stream?.Close();
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        const int RmRebootReasonNone = 0;
        const int CCH_RM_MAX_APP_NAME = 255;
        const int CCH_RM_MAX_SVC_NAME = 63;

        enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle,
                                              UInt32 nFiles,
                                              string[] rgsFilenames,
                                              UInt32 nApplications,
                                              [In] RM_UNIQUE_PROCESS[] rgApplications,
                                              UInt32 nServices,
                                              string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint dwSessionHandle,
                                    out uint pnProcInfoNeeded,
                                    ref uint pnProcInfo,
                                    [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
                                    ref uint lpdwRebootReasons);

        /// <summary>
        /// Find out what process(es) have a lock on the specified file.
        /// </summary>
        /// <param name="path">Path of the file.</param>
        /// <returns>Processes locking the file</returns>
        /// <remarks>See also:
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa373661(v=vs.85).aspx
        /// http://wyupdate.googlecode.com/svn-history/r401/trunk/frmFilesInUse.cs (no copyright in code at time of viewing)
        /// 
        /// </remarks>
        public static IEnumerable<Process> WhoIsLocking(string path)
        {
            uint handle;
            string key = Guid.NewGuid().ToString();
            var processes = new List<Process>();

            int res = RmStartSession(out handle, 0, key);

            if (res != 0) {
                // Could not begin restart session. Unable to determine file locker.
                return Enumerable.Empty<Process>();
            }

            try
            {
                const int ERROR_MORE_DATA = 234;
                uint pnProcInfoNeeded = 0,
                     pnProcInfo = 0,
                     lpdwRebootReasons = RmRebootReasonNone;

                string[] resources = new string[] { path }; // Just checking on one resource.

                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);

                if (res != 0) {
                    return Enumerable.Empty<Process>();
                }

                //Note: there's a race condition here -- the first call to RmGetList() returns
                //      the total number of process. However, when we call RmGetList() again to get
                //      the actual processes this number may have increased.
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

                if (res == ERROR_MORE_DATA) {
                    // Create an array to store the process results
                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;

                    // Get the list
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);

                    if (res == 0)
                    {
                        processes = new List<Process>((int)pnProcInfo);

                        // Enumerate all of the results and add them to the 
                        // list to be returned
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                            }
                            // catch the error -- in case the process is no longer running
                            catch (ArgumentException) { }
                        }
                    }
                }
                else if (res != 0) {
                    // Could not list processes locking resource. Failed to get size of result.
                    return Enumerable.Empty<Process>();
                }
            }
            finally
            {
                RmEndSession(handle);
            }

            return processes;
        }
    }
}