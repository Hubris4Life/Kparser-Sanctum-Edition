using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WaywardGamers.KParser.Bridge;

namespace StatCaptureProbe
{
    internal static class Program
    {
        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint MemCommit = 0x1000;
        private const uint PageGuard = 0x100;
        private const uint PageNoAccess = 0x01;
        private const int PlayerBytes = 1080;
        private static readonly int[] PlayerSignature =
        {
            0x6A, 0x01, 0x8D, 0x44, 0x24, 0x2C, 0x68,
            0x80, 0x80, 0x80, 0x80, 0x66, 0x83, -1, -1,
            0x50, 0x66, 0x83, -1, -1, 0x51, 0x52, 0xE8,
            -1, -1, -1, -1, 0xA1
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualQueryEx(
            IntPtr process,
            IntPtr address,
            out MemoryBasicInformation information,
            IntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr address,
            byte[] buffer,
            IntPtr size,
            out IntPtr bytesRead);

        private static int Main(string[] args)
        {
            int preferredProcessId = 0;
            if (args.Length > 0)
                int.TryParse(args[0], out preferredProcessId);
            string reportPath = args.Length > 1 ? args[1] : string.Empty;
            StringWriter report = new StringWriter();

            try
            {
                SanctumPlayerStatProfile profile =
                    SanctumPlayerStatsReader.Capture("LiveProbe", preferredProcessId);
                report.WriteLine(string.Format(
                    "captured pid={0} job={1}/{2} str={3} dex={4} vit={5} agi={6} int={7} mnd={8} chr={9} enfeebling={10} elemental={11} dark={12} ninjutsu={13} singing={14}",
                    profile.ProcessId,
                    profile.MainJob,
                    profile.MainJobLevel,
                    profile.Strength,
                    profile.Dexterity,
                    profile.Vitality,
                    profile.Agility,
                    profile.Intelligence,
                    profile.Mind,
                    profile.Charisma,
                    profile.EnfeeblingSkill,
                    profile.ElementalSkill,
                    profile.DarkSkill,
                    profile.NinjutsuSkill,
                    profile.SingingSkill));
                return Finish(report, reportPath, 0);
            }
            catch (Exception ex)
            {
                int level = 0;
                while (ex != null)
                {
                    report.WriteLine("error[{0}]={1}: {2}", level, ex.GetType().Name, ex.Message);
                    ex = ex.InnerException;
                    level++;
                }
                if (preferredProcessId > 0)
                {
                    try
                    {
                        InspectCandidate(preferredProcessId, 0x050B33C8, report);
                        InspectModuleReferences(preferredProcessId, 0x050B33C8, report);
                    }
                    catch (Exception scanError)
                    {
                        report.WriteLine("scan-error={0}: {1}", scanError.GetType().Name, scanError.Message);
                    }
                }
                return Finish(report, reportPath, 1);
            }
        }

        private static void InspectModuleReferences(int processId, uint candidateAddress, StringWriter report)
        {
            Process target = Process.GetProcessById(processId);
            IntPtr process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
            if (process == IntPtr.Zero)
                throw new InvalidOperationException("The module inspection could not open the process.");

            try
            {
                MemoryBasicInformation candidateRegion;
                if (VirtualQueryEx(process, new IntPtr(unchecked((int)candidateAddress)), out candidateRegion,
                    new IntPtr(Marshal.SizeOf(typeof(MemoryBasicInformation)))) != IntPtr.Zero)
                {
                    report.WriteLine(
                        "candidate-region base=0x{0:X8} allocation=0x{1:X8} size={2} protect=0x{3:X} type=0x{4:X}",
                        candidateRegion.BaseAddress.ToInt64(),
                        candidateRegion.AllocationBase.ToInt64(),
                        candidateRegion.RegionSize.ToInt64(),
                        candidateRegion.Protect,
                        candidateRegion.Type);
                }

                foreach (ProcessModule module in target.Modules)
                {
                    if (string.Equals(module.ModuleName, "FFXiMain.dll", StringComparison.OrdinalIgnoreCase) == false)
                        continue;

                    long moduleBase = module.BaseAddress.ToInt64();
                    int moduleSize = module.ModuleMemorySize;
                    report.WriteLine("ffximain base=0x{0:X8} size={1} path={2}", moduleBase, moduleSize, module.FileName);
                    ScanModule(process, moduleBase, moduleSize, candidateAddress, report);
                }
            }
            finally
            {
                target.Dispose();
                CloseHandle(process);
            }
        }

        private static void ScanModule(
            IntPtr process,
            long moduleBase,
            int moduleSize,
            uint candidateAddress,
            StringWriter report)
        {
            const int chunkSize = 0x10000;
            byte[] carry = new byte[0];
            int signatureCount = 0;
            int referenceCount = 0;
            byte[] targetBytes = BitConverter.GetBytes(candidateAddress);

            for (int moduleOffset = 0; moduleOffset < moduleSize; moduleOffset += chunkSize)
            {
                int requested = Math.Min(chunkSize, moduleSize - moduleOffset);
                byte[] chunk = new byte[requested];
                IntPtr bytesRead;
                if (ReadProcessMemory(process, new IntPtr(moduleBase + moduleOffset), chunk,
                    new IntPtr(requested), out bytesRead) == false || bytesRead.ToInt32() <= 0)
                {
                    carry = new byte[0];
                    continue;
                }

                int actual = bytesRead.ToInt32();
                byte[] scan = new byte[carry.Length + actual];
                Buffer.BlockCopy(carry, 0, scan, 0, carry.Length);
                Buffer.BlockCopy(chunk, 0, scan, carry.Length, actual);
                long scanBase = moduleBase + moduleOffset - carry.Length;

                for (int offset = 0; offset <= scan.Length - PlayerSignature.Length - 4; offset++)
                {
                    if (MatchesSignature(scan, offset) == false)
                        continue;

                    signatureCount++;
                    uint operand = BitConverter.ToUInt32(scan, offset + 28);
                    byte[] indirect = new byte[4];
                    IntPtr indirectRead;
                    uint value = 0;
                    if (ReadProcessMemory(process, new IntPtr(unchecked((int)operand)), indirect,
                        new IntPtr(4), out indirectRead) && indirectRead.ToInt32() == 4)
                    {
                        value = BitConverter.ToUInt32(indirect, 0);
                    }
                    report.WriteLine(
                        "player-signature[{0}] at=0x{1:X8} operand=0x{2:X8} indirect=0x{3:X8} bytes={4}",
                        signatureCount,
                        scanBase + offset,
                        operand,
                        value,
                        BitConverter.ToString(scan, offset, Math.Min(40, scan.Length - offset)));
                }

                for (int offset = 0; offset <= scan.Length - targetBytes.Length; offset++)
                {
                    if (scan[offset] != targetBytes[0] || scan[offset + 1] != targetBytes[1] ||
                        scan[offset + 2] != targetBytes[2] || scan[offset + 3] != targetBytes[3])
                    {
                        continue;
                    }

                    referenceCount++;
                    if (referenceCount <= 32)
                        report.WriteLine("candidate-module-reference[{0}]=0x{1:X8}", referenceCount, scanBase + offset);
                }

                int carryLength = Math.Min(64, scan.Length);
                carry = new byte[carryLength];
                Buffer.BlockCopy(scan, scan.Length - carryLength, carry, 0, carryLength);
            }

            report.WriteLine("player-signatures={0} candidate-module-references={1}", signatureCount, referenceCount);
        }

        private static bool MatchesSignature(byte[] data, int offset)
        {
            for (int index = 0; index < PlayerSignature.Length; index++)
            {
                if (PlayerSignature[index] >= 0 && data[offset + index] != PlayerSignature[index])
                    return false;
            }
            return true;
        }

        private static void InspectCandidate(int processId, long address, StringWriter report)
        {
            IntPtr process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
            if (process == IntPtr.Zero)
                throw new InvalidOperationException("The candidate inspection could not open the process.");

            try
            {
                byte[] data = new byte[PlayerBytes];
                IntPtr bytesRead;
                if (ReadProcessMemory(process, new IntPtr(address), data, new IntPtr(data.Length), out bytesRead) == false ||
                    bytesRead.ToInt64() != data.Length)
                {
                    report.WriteLine("known-candidate=0x{0:X8} unreadable", address);
                    return;
                }

                report.WriteLine("known-candidate={0}", DescribeCandidate(data, 0, address));
                report.WriteLine(
                    "known-candidate exp={0}/{1} atk={2} def={3} flags=0x{4:X2} zoning={5} submap={6} strict={7}",
                    BitConverter.ToUInt16(data, 12),
                    BitConverter.ToUInt16(data, 14),
                    BitConverter.ToInt16(data, 44),
                    BitConverter.ToInt16(data, 46),
                    data[492],
                    BitConverter.ToInt32(data, 940),
                    BitConverter.ToInt32(data, 1008),
                    LooksLikePlayerData(data, 0));
                report.Write("known-candidate stats=");
                for (int index = 0; index < 7; index++)
                {
                    if (index > 0)
                        report.Write(",");
                    report.Write("{0}+{1}",
                        BitConverter.ToInt16(data, 16 + index * 2),
                        BitConverter.ToInt16(data, 30 + index * 2));
                }
                report.WriteLine();
                report.Write("known-candidate combat-skills=");
                for (int index = 0; index < 48; index++)
                {
                    if (index > 0)
                        report.Write(",");
                    report.Write(ReadSkill(data, 0, index));
                }
                report.WriteLine();
                report.Write("known-candidate buffs=");
                for (int index = 0; index < 32; index++)
                {
                    if (index > 0)
                        report.Write(",");
                    report.Write(BitConverter.ToInt16(data, 1016 + index * 2));
                }
                report.WriteLine();
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static void ScanForCandidates(int processId, StringWriter report)
        {
            IntPtr process = OpenProcess(
                ProcessQueryInformation | ProcessVmRead,
                false,
                processId);
            if (process == IntPtr.Zero)
                throw new InvalidOperationException("The candidate scan could not open the process.");

            List<string> matches = new List<string>();
            long scannedBytes = 0;
            try
            {
                long address = 0x10000;
                long maximumAddress = 0x7FFF0000;
                int mbiSize = Marshal.SizeOf(typeof(MemoryBasicInformation));
                while (address < maximumAddress)
                {
                    MemoryBasicInformation information;
                    IntPtr query = VirtualQueryEx(
                        process,
                        new IntPtr(address),
                        out information,
                        new IntPtr(mbiSize));
                    if (query == IntPtr.Zero)
                        break;

                    long baseAddress = information.BaseAddress.ToInt64();
                    long regionSize = information.RegionSize.ToInt64();
                    if (regionSize <= 0)
                        break;

                    if (information.State == MemCommit &&
                        (information.Protect & PageGuard) == 0 &&
                        (information.Protect & PageNoAccess) == 0)
                    {
                        ScanRegion(process, baseAddress, regionSize, matches, ref scannedBytes);
                    }

                    long nextAddress = baseAddress + regionSize;
                    if (nextAddress <= address)
                        break;
                    address = nextAddress;
                }
            }
            finally
            {
                CloseHandle(process);
            }

            report.WriteLine("validated-memory-scan-bytes={0}", scannedBytes);
            report.WriteLine("validated-memory-candidates={0}", matches.Count);
            foreach (string match in matches)
                report.WriteLine("candidate=" + match);
        }

        private static void ScanRegion(
            IntPtr process,
            long regionBase,
            long regionSize,
            List<string> matches,
            ref long scannedBytes)
        {
            const int chunkSize = 1024 * 1024;
            long regionOffset = 0;
            byte[] overlap = new byte[0];

            while (regionOffset < regionSize)
            {
                int requested = (int)Math.Min(chunkSize, regionSize - regionOffset);
                byte[] chunk = new byte[requested];
                IntPtr bytesReadPointer;
                if (ReadProcessMemory(
                    process,
                    new IntPtr(regionBase + regionOffset),
                    chunk,
                    new IntPtr(requested),
                    out bytesReadPointer) == false)
                {
                    break;
                }

                int bytesRead = bytesReadPointer.ToInt32();
                if (bytesRead <= 0)
                    break;
                scannedBytes += bytesRead;

                byte[] scan = new byte[overlap.Length + bytesRead];
                if (overlap.Length > 0)
                    Buffer.BlockCopy(overlap, 0, scan, 0, overlap.Length);
                Buffer.BlockCopy(chunk, 0, scan, overlap.Length, bytesRead);

                for (int offset = 0; offset <= scan.Length - PlayerBytes; offset += 2)
                {
                    if (LooksLikePlayerData(scan, offset))
                    {
                        long candidateAddress = regionBase + regionOffset - overlap.Length + offset;
                        string description = DescribeCandidate(scan, offset, candidateAddress);
                        if (matches.Contains(description) == false)
                            matches.Add(description);
                        if (matches.Count >= 32)
                            return;
                    }
                }

                int overlapLength = Math.Min(PlayerBytes - 1, scan.Length);
                overlap = new byte[overlapLength];
                Buffer.BlockCopy(scan, scan.Length - overlapLength, overlap, 0, overlapLength);
                regionOffset += bytesRead;
            }
        }

        private static bool LooksLikePlayerData(byte[] data, int offset)
        {
            uint maxHp = BitConverter.ToUInt32(data, offset);
            uint maxMp = BitConverter.ToUInt32(data, offset + 4);
            int mainJob = data[offset + 8];
            int mainLevel = data[offset + 9];
            int subJob = data[offset + 10];
            int subLevel = data[offset + 11];
            if (maxHp == 0 || maxHp > 200000 || maxMp > 100000 ||
                mainJob < 1 || mainJob > 24 || mainLevel < 1 || mainLevel > 99 ||
                subJob < 0 || subJob > 24 || subLevel < 0 || subLevel > 99)
            {
                return false;
            }

            for (int index = 0; index < 7; index++)
            {
                int baseStat = BitConverter.ToInt16(data, offset + 16 + index * 2);
                int modifier = BitConverter.ToInt16(data, offset + 30 + index * 2);
                int total = baseStat + modifier;
                if (baseStat <= 0 || baseStat > 5000 || modifier < -5000 ||
                    modifier > 20000 || total <= 0 || total > 30000)
                {
                    return false;
                }
            }

            int nonzeroSkills = 0;
            for (int index = 0; index < 48; index++)
            {
                int skill = BitConverter.ToUInt16(data, offset + 108 + index * 2) & 0x7FFF;
                if (skill > 1000)
                    return false;
                if (skill > 0)
                    nonzeroSkills++;
            }

            if (nonzeroSkills < 3)
                return false;

            int attack = BitConverter.ToInt16(data, offset + 44);
            int defense = BitConverter.ToInt16(data, offset + 46);
            if (attack < 0 || attack > 20000 || defense < 0 || defense > 20000)
                return false;

            int zoning = BitConverter.ToInt32(data, offset + 940);
            if (zoning != 0 && zoning != 1)
                return false;

            for (int index = 0; index < 32; index++)
            {
                int buff = BitConverter.ToInt16(data, offset + 1016 + index * 2);
                if (buff < -1 || buff > 4096)
                    return false;
            }

            return true;
        }

        private static string DescribeCandidate(byte[] data, int offset, long address)
        {
            return string.Format(
                "0x{0:X8} hp={1} mp={2} job={3}/{4} sub={5}/{6} int={7}+{8} enfeebling={9} elemental={10} dark={11} ninjutsu={12} singing={13}",
                address,
                BitConverter.ToUInt32(data, offset),
                BitConverter.ToUInt32(data, offset + 4),
                data[offset + 8],
                data[offset + 9],
                data[offset + 10],
                data[offset + 11],
                BitConverter.ToInt16(data, offset + 24),
                BitConverter.ToInt16(data, offset + 38),
                ReadSkill(data, offset, 36),
                ReadSkill(data, offset, 37),
                ReadSkill(data, offset, 38),
                ReadSkill(data, offset, 40),
                ReadSkill(data, offset, 41));
        }

        private static int ReadSkill(byte[] data, int offset, int index)
        {
            return BitConverter.ToUInt16(data, offset + 108 + index * 2) & 0x7FFF;
        }

        private static int Finish(StringWriter report, string reportPath, int exitCode)
        {
            string text = report.ToString();
            Console.Write(text);
            if (string.IsNullOrEmpty(reportPath) == false)
                File.WriteAllText(Path.GetFullPath(reportPath), text);
            return exitCode;
        }
    }
}
