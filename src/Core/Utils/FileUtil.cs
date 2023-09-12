using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
namespace Nekres.Music_Mixer {
    internal static class FileUtil
    {
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
            } catch (Exception) {
                return false;
            } finally {
                stream?.Dispose();
            }
            return false;
        }
    }
}
