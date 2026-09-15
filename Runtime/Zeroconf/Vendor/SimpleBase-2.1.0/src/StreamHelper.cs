// <copyright file="StreamHelper.cs" company="Sedat Kapanoglu">
// Copyright (c) 2014-2019 Sedat Kapanoglu
// Licensed under Apache-2.0 License (see LICENSE.txt file for details)
// </copyright>

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleBase
{
    /// <summary>
    /// Provides Stream functionality to any buffer-based encoding operation.
    /// </summary>
    internal static class StreamHelper
    {
        private const int defaultBufferSize = 4096;

        public static void Encode(
            Stream input,
            TextWriter output,
            Func<byte[], bool, string> bufferEncodeFunc,
            int bufferSize = defaultBufferSize)
        {
            var buffer = new byte[bufferSize];
            while (true)
            {
                int bytesRead = input.Read(buffer, 0, bufferSize);
                if (bytesRead < 1)
                {
                    break;
                }

                byte[] readBuffer = new byte[bytesRead];
                Array.Copy(buffer, readBuffer, bytesRead);
                var result = bufferEncodeFunc(readBuffer, bytesRead < bufferSize);
                output.Write(result);
            }
        }

        public static async Task EncodeAsync(
            Stream input,
            TextWriter output,
            Func<byte[], bool, string> bufferEncodeFunc,
            int bufferSize = defaultBufferSize)
        {
            var buffer = new byte[bufferSize];
            while (true)
            {
                int bytesRead = await input.ReadAsync(buffer, 0, bufferSize).ConfigureAwait(false);
                if (bytesRead < 1)
                {
                    break;
                }

                byte[] readBuffer = new byte[bytesRead];
                Array.Copy(buffer, readBuffer, bytesRead);
                var result = bufferEncodeFunc(readBuffer, bytesRead < bufferSize);
                await output.WriteAsync(result).ConfigureAwait(false);
            }
        }

        public static void Decode(
            TextReader input,
            Stream output,
            Func<string, byte[]> decodeBufferFunc,
            int bufferSize = defaultBufferSize)
        {
            var buffer = new char[bufferSize];
            while (true)
            {
                int bytesRead = input.Read(buffer, 0, bufferSize);
                if (bytesRead < 1)
                {
                    break;
                }

                char[] readBuffer = new char[bytesRead];
                Array.Copy(buffer, readBuffer, bytesRead);
                var result = decodeBufferFunc(new string(readBuffer));
                output.Write(result, 0, result.Length);
            }
        }

        public static async Task DecodeAsync(
            TextReader input,
            Stream output,
            Func<string, byte[]> decodeBufferFunc,
            int bufferSize = defaultBufferSize)
        {
            var buffer = new char[bufferSize];
            while (true)
            {
                int bytesRead = await input.ReadAsync(buffer, 0, bufferSize).ConfigureAwait(false);
                if (bytesRead < 1)
                {
                    break;
                }

                char[] readBuffer = new char[bytesRead];
                Array.Copy(buffer, readBuffer, bytesRead);
                var result = decodeBufferFunc(new string(readBuffer));
                await output.WriteAsync(result.ToArray(), 0, result.Length).ConfigureAwait(false);
            }
        }
    }
}