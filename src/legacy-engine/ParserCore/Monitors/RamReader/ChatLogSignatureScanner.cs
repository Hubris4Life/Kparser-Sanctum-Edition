// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WaywardGamers.KParser.Monitoring.Memory;

namespace WaywardGamers.KParser.Monitoring
{
    /// <summary>
    /// Finds the instruction that references FFXI's chat-log root pointer and
    /// converts its absolute operand into the module-relative offset KParser uses.
    /// </summary>
    internal static class ChatLogSignatureScanner
    {
        private const int BlockSize = 0x10000;

        private static readonly byte[] Signature =
        {
            0x8B, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x85, 0xC9, 0x74,
            0x0F, 0x8B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8B
        };

        private const string SignatureMask = "xx????xxxxx?????x";

        internal static IList<uint> FindCandidateOffsets(POL pol)
        {
            List<uint> candidates = new List<uint>();
            HashSet<uint> seen = new HashSet<uint>();

            if ((pol == null) || (pol.Process == null) ||
                (pol.FFXIBaseAddress == IntPtr.Zero) || (pol.FFXIModuleSize <= 0))
            {
                return candidates;
            }

            uint moduleBase = unchecked((uint)pol.FFXIBaseAddress.ToInt32());
            uint moduleSize = unchecked((uint)pol.FFXIModuleSize);

            for (uint blockOffset = 0; blockOffset < moduleSize; blockOffset += BlockSize)
            {
                uint remaining = moduleSize - blockOffset;
                uint bytesToRead = Math.Min(
                    remaining,
                    unchecked((uint)(BlockSize + Signature.Length - 1)));

                byte[] buffer = new byte[bytesToRead];
                IntPtr readAddress = Pointers.IncrementPointer(pol.FFXIBaseAddress, blockOffset);

                using (ProcessMemoryReading reader =
                    new ProcessMemoryReading(pol.Process.Handle, readAddress, bytesToRead))
                {
                    if (reader.ReadBufferPtr == IntPtr.Zero)
                        continue;

                    Marshal.Copy(reader.ReadBufferPtr, buffer, 0, buffer.Length);
                }

                int finalStart = buffer.Length - Signature.Length;
                for (int index = 0; index <= finalStart; index++)
                {
                    if (!Matches(buffer, index))
                        continue;

                    uint absoluteOperand = BitConverter.ToUInt32(buffer, index + 2);
                    long relativeOffset = (long)absoluteOperand + 0x0C - moduleBase;

                    if ((relativeOffset < 0) || (relativeOffset >= moduleSize))
                        continue;

                    uint candidate = unchecked((uint)relativeOffset);
                    if (seen.Add(candidate))
                        candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private static bool Matches(byte[] buffer, int startIndex)
        {
            for (int index = 0; index < Signature.Length; index++)
            {
                if ((SignatureMask[index] == 'x') &&
                    (buffer[startIndex + index] != Signature[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
