using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnityPackage.Extension
{
    public static class TarOutputStreamExtensions
    {
        /// <summary>
        /// Writes a file to the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="source"></param>
        /// <param name="dest"></param>
        public static void WriteFile(this TarOutputStream stream, string source, string dest)
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
                    int numRead = inputStream.Read(localBuffer, 0, localBuffer.Length);
                    if (numRead <= 0)
                        break;

                    stream.Write(localBuffer, 0, numRead);
                }

                //Close the entry
                stream.CloseEntry();
            }
        }

        /// <summary>
        /// Writes a text file to the stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="dest"></param>
        /// <param name="content"></param>
        public static void WriteAllText(this TarOutputStream stream, string dest, string content)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            
            TarEntry entry = TarEntry.CreateTarEntry(dest);
            entry.Size = bytes.Length;

            // Add the entry to the tar stream, before writing the data.
            stream.PutNextEntry(entry);

            // this is copied from TarArchive.WriteEntryCore
            stream.Write(bytes, 0, bytes.Length);

            //Close the entry
            stream.CloseEntry();
        }

        /// <summary>
        /// Reads a file from a stream
        /// </summary>
        /// <param name="tarIn"></param>
        /// <param name="outStream"></param>
        /// <returns></returns>
        public static long ReadNextFile(this TarInputStream tarIn, Stream outStream)
        {
            long totalRead = 0;
            byte[] buffer = new byte[4096];
            bool isAscii = true;
            bool cr = false;

            int numRead = tarIn.Read(buffer, 0, buffer.Length);
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

                numRead = tarIn.Read(buffer, 0, buffer.Length);
                totalRead += numRead;
            }

            return totalRead;
        }
    }
}
