// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WaywardGamers.KParser.Monitoring;
using WaywardGamers.KParser.Monitoring.Memory;

namespace WaywardGamers.KParser.Bridge
{
    /// <summary>
    /// A validated, read-only snapshot of the local FFXI player's visible stats.
    /// Values that only exist on the game server (merits, job points and hidden
    /// equipment modifiers) are deliberately not inferred here.
    /// </summary>
    public sealed class SanctumPlayerStatProfile
    {
        public string PlayerName { get; set; }
        public int ProcessId { get; set; }
        public int MainJob { get; set; }
        public int MainJobLevel { get; set; }
        public int SubJob { get; set; }
        public int SubJobLevel { get; set; }
        public int Strength { get; set; }
        public int Dexterity { get; set; }
        public int Vitality { get; set; }
        public int Agility { get; set; }
        public int Intelligence { get; set; }
        public int Mind { get; set; }
        public int Charisma { get; set; }
        public int DaggerSkill { get; set; }
        public int SwordSkill { get; set; }
        public int KatanaSkill { get; set; }
        public int ClubSkill { get; set; }
        public int ArcherySkill { get; set; }
        public int MarksmanshipSkill { get; set; }
        public int EnfeeblingSkill { get; set; }
        public int ElementalSkill { get; set; }
        public int DarkSkill { get; set; }
        public int NinjutsuSkill { get; set; }
        public int SingingSkill { get; set; }
        public DateTime CapturedUtc { get; set; }
    }

    /// <summary>
    /// Holds the one local-player profile selected by the user for this engine
    /// session. The parser can still estimate every other combatant normally.
    /// </summary>
    public static class SanctumDotProfileStore
    {
        private static readonly object SyncRoot = new object();
        private static SanctumPlayerStatProfile current;
        private static long revision;

        public static void Set(SanctumPlayerStatProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");

            lock (SyncRoot)
            {
                current = profile;
                revision++;
            }
        }

        internal static SanctumPlayerStatProfile GetForActor(string actorName)
        {
            lock (SyncRoot)
            {
                return current != null &&
                       string.Equals(
                           current.PlayerName,
                           actorName,
                           StringComparison.OrdinalIgnoreCase)
                    ? current
                    : null;
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                current = null;
                revision++;
            }
        }

        internal static long Revision
        {
            get
            {
                lock (SyncRoot)
                    return revision;
            }
        }

        internal static string CurrentPlayerName
        {
            get
            {
                lock (SyncRoot)
                    return current == null ? string.Empty : current.PlayerName;
            }
        }
    }

    /// <summary>
    /// Locates FFXI's STATUS_DATA structure using its established instruction
    /// signature, then reads only the small portion containing player stats and
    /// combat skills. The process handle requests query/read rights only.
    /// </summary>
    public static class SanctumPlayerStatsReader
    {
        private const int BlockSize = 0x10000;
        private const int PlayerBytesToRead = 204;
        private const int PlayerPointerOperandOffset = 28;

        private static readonly int[] PlayerSignature =
        {
            0x6A, 0x01, 0x8D, 0x44, 0x24, 0x2C, 0x68,
            0x80, 0x80, 0x80, 0x80, 0x66, 0x83, -1, -1,
            0x50, 0x66, 0x83, -1, -1, 0x51, 0x52, 0xE8,
            -1, -1, -1, -1, 0xA1
        };

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            ProcessAccessRights desiredAccess,
            bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static SanctumPlayerStatProfile Capture(
            string playerName,
            int preferredProcessId)
        {
            if (string.IsNullOrEmpty(playerName) || playerName.Trim().Length == 0)
                throw new ArgumentException("Select your player row before capturing stats.", "playerName");

            string normalizedName = playerName.Trim();
            if (normalizedName.Length > 32)
                throw new ArgumentException("The selected player name is not valid.", "playerName");

            Process[] processes = ProcessAccess.FindFFXIProcesses();
            if (processes == null || processes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No accessible FFXI client was found. Log fully into the selected server and run both applications at the same privilege.");
            }

            List<SanctumPlayerStatProfile> captured =
                new List<SanctumPlayerStatProfile>();
            Exception lastError = null;

            try
            {
                Array.Sort(processes, delegate(Process left, Process right)
                {
                    if (left.Id == preferredProcessId)
                        return -1;
                    if (right.Id == preferredProcessId)
                        return 1;
                    return left.Id.CompareTo(right.Id);
                });

                foreach (Process process in processes)
                {
                    try
                    {
                        SanctumPlayerStatProfile profile = CaptureFromProcess(
                            process,
                            normalizedName);
                        captured.Add(profile);

                        if (process.Id == preferredProcessId)
                            return profile;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    if (process != null)
                        process.Dispose();
                }
            }

            if (captured.Count == 1)
                return captured[0];
            if (captured.Count > 1)
            {
                throw new InvalidOperationException(
                    "More than one logged-in FFXI client was found. Start the parser on the desired client, then capture again.");
            }

            throw new InvalidOperationException(
                "FFXI's player stat structure could not be found or validated. " +
                "Make sure a character is fully logged in and try again.",
                lastError);
        }

        private static SanctumPlayerStatProfile CaptureFromProcess(
            Process process,
            string playerName)
        {
            IntPtr moduleBase;
            int moduleSize;
            if (ProcessAccess.TryGetFFXIModule(process, out moduleBase, out moduleSize) == false)
                throw new InvalidOperationException("FFXiMain.dll is not available in the selected process.");

            IntPtr readOnlyHandle = OpenProcess(
                ProcessAccessRights.QueryInformation | ProcessAccessRights.VMRead,
                false,
                process.Id);
            if (readOnlyHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "The FFXI process could not be opened for read-only stat capture.");
            }

            try
            {
                uint moduleBytes = unchecked((uint)moduleSize);
                int overlap = PlayerSignature.Length + sizeof(uint) - 1;
                int signatureMatches = 0;
                int readableDirectBlocks = 0;
                int readableIndirectBlocks = 0;
                int readablePlayerBlocks = 0;
                List<string> candidateSummaries = new List<string>();

                for (uint blockOffset = 0; blockOffset < moduleBytes; blockOffset += BlockSize)
                {
                    uint remaining = moduleBytes - blockOffset;
                    int bytesToRead = (int)Math.Min(
                        remaining,
                        unchecked((uint)(BlockSize + overlap)));
                    byte[] buffer = ReadBytes(
                        readOnlyHandle,
                        Pointers.IncrementPointer(moduleBase, blockOffset),
                        bytesToRead);
                    if (buffer == null)
                        continue;

                    int finalStart = buffer.Length - PlayerSignature.Length - sizeof(uint);
                    for (int index = 0; index <= finalStart; index++)
                    {
                        if (MatchesPlayerSignature(buffer, index) == false)
                            continue;

                        signatureMatches++;
                        uint playerAddress = BitConverter.ToUInt32(
                            buffer,
                            index + PlayerPointerOperandOffset);
                        byte[] playerData = ReadBytes(
                            readOnlyHandle,
                            ToPointer(playerAddress),
                            PlayerBytesToRead);
                        if (playerData != null)
                        {
                            readableDirectBlocks++;
                            readablePlayerBlocks++;
                            candidateSummaries.Add(DescribePlayerData(playerData));
                        }
                        SanctumPlayerStatProfile profile;
                        if (TryParsePlayerData(
                            playerData,
                            playerName,
                            process.Id,
                            out profile))
                        {
                            return profile;
                        }

                        // Some older/private client builds expose a storage address
                        // here instead of STATUS_DATA itself. Only try that form after
                        // the direct block fails validation, and validate it normally.
                        byte[] pointerBytes = ReadBytes(
                            readOnlyHandle,
                            ToPointer(playerAddress),
                            sizeof(uint));
                        if (pointerBytes == null)
                            continue;

                        uint indirectAddress = BitConverter.ToUInt32(pointerBytes, 0);
                        if (indirectAddress == 0 || indirectAddress == playerAddress)
                            continue;

                        byte[] indirectData = ReadBytes(
                            readOnlyHandle,
                            ToPointer(indirectAddress),
                            PlayerBytesToRead);
                        if (indirectData != null)
                        {
                            readableIndirectBlocks++;
                            readablePlayerBlocks++;
                            candidateSummaries.Add(DescribePlayerData(indirectData));
                        }
                        if (TryParsePlayerData(
                            indirectData,
                            playerName,
                            process.Id,
                            out profile))
                        {
                            return profile;
                        }
                    }
                }

                throw new InvalidOperationException(string.Format(
                    "The player-stat scan found {0} signature match(es), {1} direct block(s), {2} indirect block(s), and {3} readable candidate block(s), but none passed validation. Candidates: {4}",
                    signatureMatches,
                    readableDirectBlocks,
                    readableIndirectBlocks,
                    readablePlayerBlocks,
                    candidateSummaries.Count == 0
                        ? "none"
                        : string.Join(" || ", candidateSummaries.ToArray())));
            }
            finally
            {
                CloseHandle(readOnlyHandle);
            }

        }

        internal static bool TryParsePlayerData(
            byte[] data,
            string playerName,
            int processId,
            out SanctumPlayerStatProfile profile)
        {
            profile = null;
            if (data == null || data.Length < PlayerBytesToRead)
                return false;

            int mainJob = data[8];
            int mainJobLevel = data[9];
            int subJob = data[10];
            int subJobLevel = data[11];
            int strength = ReadTotalStat(data, 16, 30);
            int dexterity = ReadTotalStat(data, 18, 32);
            int vitality = ReadTotalStat(data, 20, 34);
            int agility = ReadTotalStat(data, 22, 36);
            int intelligence = ReadTotalStat(data, 24, 38);
            int mind = ReadTotalStat(data, 26, 40);
            int charisma = ReadTotalStat(data, 28, 42);
            int dagger = ReadCombatSkill(data, 2);
            int sword = ReadCombatSkill(data, 3);
            int katana = ReadCombatSkill(data, 9);
            int club = ReadCombatSkill(data, 11);
            int archery = ReadCombatSkill(data, 25);
            int marksmanship = ReadCombatSkill(data, 26);
            int enfeebling = ReadCombatSkill(data, 36);
            int elemental = ReadCombatSkill(data, 37);
            int dark = ReadCombatSkill(data, 38);
            int ninjutsu = ReadCombatSkill(data, 40);
            int singing = ReadCombatSkill(data, 41);

            if (mainJob < 1 || mainJob > 24 || mainJobLevel < 1 ||
                subJob < 0 || subJob > 24 || subJobLevel < 0 ||
                !IsValidStat(strength) || !IsValidStat(dexterity) ||
                !IsValidStat(vitality) || !IsValidStat(agility) ||
                !IsValidStat(intelligence) || !IsValidStat(mind) ||
                !IsValidStat(charisma) || !IsValidSkill(dagger) ||
                !IsValidSkill(sword) || !IsValidSkill(katana) ||
                !IsValidSkill(club) || !IsValidSkill(archery) ||
                !IsValidSkill(marksmanship) || !IsValidSkill(enfeebling) ||
                !IsValidSkill(elemental) || !IsValidSkill(dark) ||
                !IsValidSkill(ninjutsu) || !IsValidSkill(singing))
            {
                return false;
            }

            profile = new SanctumPlayerStatProfile
            {
                PlayerName = playerName,
                ProcessId = processId,
                MainJob = mainJob,
                MainJobLevel = mainJobLevel,
                SubJob = subJob,
                SubJobLevel = subJobLevel,
                Strength = strength,
                Dexterity = dexterity,
                Vitality = vitality,
                Agility = agility,
                Intelligence = intelligence,
                Mind = mind,
                Charisma = charisma,
                DaggerSkill = dagger,
                SwordSkill = sword,
                KatanaSkill = katana,
                ClubSkill = club,
                ArcherySkill = archery,
                MarksmanshipSkill = marksmanship,
                EnfeeblingSkill = enfeebling,
                ElementalSkill = elemental,
                DarkSkill = dark,
                NinjutsuSkill = ninjutsu,
                SingingSkill = singing,
                CapturedUtc = DateTime.UtcNow
            };
            return true;
        }

        private static int ReadTotalStat(byte[] data, int baseOffset, int modifierOffset)
        {
            return BitConverter.ToInt16(data, baseOffset) +
                   BitConverter.ToInt16(data, modifierOffset);
        }

        private static int ReadCombatSkill(byte[] data, int skillIndex)
        {
            ushort raw = BitConverter.ToUInt16(data, 108 + skillIndex * sizeof(ushort));
            return raw & 0x7FFF;
        }

        private static bool IsValidStat(int value)
        {
            return value > 0 && value <= 30000;
        }

        private static bool IsValidSkill(int value)
        {
            return value >= 0 && value <= 2000;
        }

        private static string DescribePlayerData(byte[] data)
        {
            if (data == null || data.Length < PlayerBytesToRead)
                return "unreadable";

            return string.Format(
                "job={0}/{1}, subjob={2}/{3}, stats={4},{5},{6},{7},{8},{9},{10}, weaponSkills={11},{12},{13},{14},{15},{16}, magicSkills={17},{18},{19},{20},{21}, bytes0-63={22}",
                data[8],
                data[9],
                data[10],
                data[11],
                ReadTotalStat(data, 16, 30),
                ReadTotalStat(data, 18, 32),
                ReadTotalStat(data, 20, 34),
                ReadTotalStat(data, 22, 36),
                ReadTotalStat(data, 24, 38),
                ReadTotalStat(data, 26, 40),
                ReadTotalStat(data, 28, 42),
                ReadCombatSkill(data, 2),
                ReadCombatSkill(data, 3),
                ReadCombatSkill(data, 9),
                ReadCombatSkill(data, 11),
                ReadCombatSkill(data, 25),
                ReadCombatSkill(data, 26),
                ReadCombatSkill(data, 36),
                ReadCombatSkill(data, 37),
                ReadCombatSkill(data, 38),
                ReadCombatSkill(data, 40),
                ReadCombatSkill(data, 41),
                BitConverter.ToString(data, 0, 64));
        }

        private static bool MatchesPlayerSignature(byte[] buffer, int start)
        {
            for (int index = 0; index < PlayerSignature.Length; index++)
            {
                int expected = PlayerSignature[index];
                if (expected >= 0 && buffer[start + index] != expected)
                    return false;
            }

            return true;
        }

        private static byte[] ReadBytes(IntPtr handle, IntPtr address, int length)
        {
            if (handle == IntPtr.Zero || address == IntPtr.Zero || length <= 0)
                return null;

            using (ProcessMemoryReading reader =
                new ProcessMemoryReading(handle, address, unchecked((uint)length)))
            {
                if (reader.ReadBufferPtr == IntPtr.Zero)
                    return null;

                byte[] bytes = new byte[length];
                Marshal.Copy(reader.ReadBufferPtr, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        private static IntPtr ToPointer(uint address)
        {
            return new IntPtr(unchecked((int)address));
        }
    }
}
