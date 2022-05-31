using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter.Package
{


    public static class TarStreamExtensions
    {
        /// <summary>
        /// Reads and writes a file to the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="source"></param>
        /// <param name="dest"></param>
        public static async Task WriteFileAsync(this TarOutputStream stream, string source, string dest)
        {
            using (Stream inputStream = File.OpenRead(source))
            {
                long fileSize = inputStream.Length;

                // Create a tar entry named as appropriate. You can set the name to anything,
                // but avoid names starting with drive or UNC.
                TarEntry entry = TarEntry.CreateTarEntry(dest);

                // Must set size, otherwise TarOutputStream will fail when output exceeds.
                entry.Size = fileSize;

                // Add the entry to the tar stream, before writing the data.
                stream.PutNextEntry(entry);

                // this is copied from TarArchive.WriteEntryCore
                byte[] localBuffer = new byte[32 * 1024];
                while (true)
                {
                    int numRead = await inputStream.ReadAsync(localBuffer, 0, localBuffer.Length);
                    if (numRead <= 0)
                        break;

                    await stream.WriteAsync(localBuffer, 0, numRead);
                }

                //Close the entry
                stream.CloseEntry();
            }
        }

        /// <summary>
        /// Writes all text to the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="dest"></param>
        /// <param name="content"></param>
        public static async Task WriteAllTextAsync(this TarOutputStream stream, string dest, string content)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);

            TarEntry entry = TarEntry.CreateTarEntry(dest);
            entry.Size = bytes.Length;

            // Add the entry to the tar stream, before writing the data.
            stream.PutNextEntry(entry);

            // this is copied from TarArchive.WriteEntryCore
            await stream.WriteAsync(bytes, 0, bytes.Length);

            //Close the entry
            stream.CloseEntry();
        }

        /// <summary>
        /// Reads the next file in the stream
        /// </summary>
        /// <param name="tarIn"></param>
        /// <param name="outStream"></param>
        /// <returns></returns>
        public async static Task<long> ReadNextFileAsync(this TarInputStream tarIn, Stream outStream)
        {
            long totalRead = 0;
            byte[] buffer = new byte[4096];
            bool isAscii = true;
            bool cr = false;

            int numRead = await tarIn.ReadAsync(buffer, 0, buffer.Length);
            int maxCheck = Math.Min(200, numRead);

            totalRead += numRead;

            for (int i = 0; i < maxCheck; i++)
            {
                byte b = buffer[i];
                if (b < 8 || (b > 13 && b < 32) || b == 255)
                {
                    isAscii = false;
                    break;
                }
            }

            while (numRead > 0)
            {
                if (isAscii)
                {
                    // Convert LF without CR to CRLF. Handle CRLF split over buffers.
                    for (int i = 0; i < numRead; i++)
                    {
                        byte b = buffer[i];     // assuming plain Ascii and not UTF-16
                        if (b == 10 && !cr)     // LF without CR
                            outStream.WriteByte(13);
                        cr = (b == 13);

                        outStream.WriteByte(b);
                    }
                }
                else
                    outStream.Write(buffer, 0, numRead);

                numRead = await tarIn.ReadAsync(buffer, 0, buffer.Length);
                totalRead += numRead;
            }

            return totalRead;
        }
    }
}
