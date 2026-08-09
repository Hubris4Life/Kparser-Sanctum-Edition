// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WaywardGamers.KParser.Bridge
{
    internal sealed class SanctumCraftingAttempt
    {
        internal SanctumCraftingAttempt()
        {
            Crafter = "You";
            Item = "Unknown synthesis";
            SkillName = string.Empty;
            LostMaterials = new List<string>();
            ResultText = string.Empty;
        }

        internal int RecordLogId { get; set; }
        internal DateTime Timestamp { get; set; }
        internal string Crafter { get; set; }
        internal string Item { get; set; }
        internal int Yield { get; set; }
        internal bool Success { get; set; }
        internal bool HighQuality { get; set; }
        internal bool IsDesynthesis { get; set; }
        internal string SkillName { get; set; }
        internal double SkillGain { get; set; }
        internal double SkillLevel { get; set; }
        internal List<string> LostMaterials { get; private set; }
        internal string ResultText { get; set; }
    }

    internal sealed class SanctumCraftingSession
    {
        internal SanctumCraftingSession()
        {
            Attempts = new List<SanctumCraftingAttempt>();
        }

        internal int Id { get; set; }
        internal DateTime Start { get; set; }
        internal DateTime End { get; set; }
        internal List<SanctumCraftingAttempt> Attempts { get; private set; }
    }

    internal static class SanctumCraftingTracker
    {
        private static readonly Regex TimestampMarker = new Regex(
            @"\[[0-9]{1,2}:[0-9]{2}:[0-9]{2}\]",
            RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(
            @"\s+",
            RegexOptions.Compiled);
        private static readonly Regex Success = new Regex(
            @"^(?<crafter>You|[A-Za-z][A-Za-z0-9'_\-]{1,23})\s+(?:successfully\s+)?(?<kind>synthesi[sz]es?|synthesi[sz]ed|desynthesi[sz]es?|desynthesi[sz]ed)\s+(?:(?<count>[0-9]+)\s+)?(?:(?:an?|the|some)\s+)?(?<item>.+?)[.!]?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Failure = new Regex(
            @"(?:(?<crafter>You|[A-Za-z][A-Za-z0-9'_\-]{1,23})\s+)?(?:(?<direct>fails?|failed)\s+to\s+(?<kind>desynthesi[sz]e|synthesi[sz]e)|(?<kind2>desynthesis|synthesis)\s+(?:fails?|failed))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FailureItem = new Regex(
            @"fails?(?:ed)?\s+to\s+(?:desynthesi[sz]e|synthesi[sz]e)\s+(?:(?:an?|the|some)\s+)?(?<item>.+?)[.!]?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SkillGain = new Regex(
            @"Your\s+(?<skill>woodworking|smithing|goldsmithing|clothcraft|leathercraft|bonecraft|alchemy|cooking)\s+skill\s+(?:rises|increases|improves)(?:\s+by)?\s+(?<gain>[0-9]+(?:\.[0-9]+)?)\s+points?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SkillLevel = new Regex(
            @"Your\s+(?<skill>woodworking|smithing|goldsmithing|clothcraft|leathercraft|bonecraft|alchemy|cooking)\s+skill\s+(?:reaches|attains)(?:\s+level)?\s+(?<level>[0-9]+(?:\.[0-9]+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LostMaterial = new Regex(
            @"You\s+(?:lose|lost)\s+(?:(?<count>[0-9]+)\s+)?(?:(?:an?|the|some)\s+)?(?<item>[^.!]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static List<SanctumCraftingAttempt> Parse(
            IEnumerable<KPDatabaseDataSet.RecordLogRow> recordRows)
        {
            List<SanctumCraftingAttempt> attempts = new List<SanctumCraftingAttempt>();
            SanctumCraftingAttempt current = null;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (KPDatabaseDataSet.RecordLogRow row in recordRows
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.RecordLogID))
            {
                string text = ExtractVisibleText(row.MessageText);
                if (text.Length == 0)
                    continue;

                string duplicateKey = row.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + text;
                if (seen.Add(duplicateKey) == false)
                    continue;

                SanctumCraftingAttempt parsed;
                if (TryParseAttempt(row.RecordLogID, row.Timestamp, text, out parsed))
                {
                    attempts.Add(parsed);
                    current = parsed;
                    AttachLostMaterials(current, text);
                    continue;
                }

                if (current == null || Math.Abs((row.Timestamp - current.Timestamp).TotalSeconds) > 20.0)
                    continue;

                Match gain = SkillGain.Match(text);
                if (gain.Success)
                {
                    current.SkillName = NormalizeCraftName(gain.Groups["skill"].Value);
                    current.SkillGain += ParseDouble(gain.Groups["gain"].Value);
                    continue;
                }

                Match level = SkillLevel.Match(text);
                if (level.Success)
                {
                    current.SkillName = NormalizeCraftName(level.Groups["skill"].Value);
                    current.SkillLevel = ParseDouble(level.Groups["level"].Value);
                    continue;
                }

                if (current.Success == false)
                    AttachLostMaterials(current, text);
            }

            return attempts;
        }

        internal static List<SanctumCraftingSession> CreateSessions(
            IEnumerable<SanctumCraftingAttempt> attempts)
        {
            List<SanctumCraftingSession> sessions = new List<SanctumCraftingSession>();
            SanctumCraftingSession current = null;
            foreach (SanctumCraftingAttempt attempt in attempts
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.RecordLogId))
            {
                if (current == null || (attempt.Timestamp - current.End).TotalMinutes >= 30.0)
                {
                    current = new SanctumCraftingSession
                    {
                        Id = attempt.RecordLogId,
                        Start = attempt.Timestamp,
                        End = attempt.Timestamp
                    };
                    sessions.Add(current);
                }

                current.Attempts.Add(attempt);
                current.End = attempt.Timestamp;
            }
            return sessions;
        }

        internal static string ExtractVisibleText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            int terminator = raw.IndexOf('\x7f');
            if (terminator >= 0)
                raw = raw.Substring(0, terminator);

            Match marker = TimestampMarker.Match(raw);
            if (marker.Success)
                raw = raw.Substring(marker.Index + marker.Length);

            StringBuilder builder = new StringBuilder(raw.Length);
            foreach (char character in raw)
            {
                if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
                    builder.Append(' ');
                else
                    builder.Append(character);
            }

            return Whitespace.Replace(builder.ToString(), " ").Trim();
        }

        private static bool TryParseAttempt(
            int recordLogId,
            DateTime timestamp,
            string text,
            out SanctumCraftingAttempt attempt)
        {
            Match success = Success.Match(text);
            if (success.Success)
            {
                int yield;
                if (int.TryParse(success.Groups["count"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out yield) == false)
                {
                    yield = 1;
                }

                string item = CleanItemName(success.Groups["item"].Value);
                attempt = new SanctumCraftingAttempt
                {
                    RecordLogId = recordLogId,
                    Timestamp = timestamp,
                    Crafter = CleanCrafter(success.Groups["crafter"].Value),
                    Item = item,
                    Yield = Math.Max(1, yield),
                    Success = true,
                    HighQuality = IsHighQuality(text, item),
                    IsDesynthesis = success.Groups["kind"].Value.StartsWith("de", StringComparison.OrdinalIgnoreCase),
                    ResultText = text
                };
                return true;
            }

            Match failure = Failure.Match(text);
            if (failure.Success)
            {
                Match itemMatch = FailureItem.Match(text);
                string item = itemMatch.Success
                    ? CleanItemName(itemMatch.Groups["item"].Value)
                    : "Unknown synthesis";
                string kind = failure.Groups["kind"].Success
                    ? failure.Groups["kind"].Value
                    : failure.Groups["kind2"].Value;
                attempt = new SanctumCraftingAttempt
                {
                    RecordLogId = recordLogId,
                    Timestamp = timestamp,
                    Crafter = CleanCrafter(failure.Groups["crafter"].Value),
                    Item = item,
                    Yield = 0,
                    Success = false,
                    HighQuality = false,
                    IsDesynthesis = kind.StartsWith("de", StringComparison.OrdinalIgnoreCase),
                    ResultText = text
                };
                return true;
            }

            attempt = null;
            return false;
        }

        private static void AttachLostMaterials(SanctumCraftingAttempt attempt, string text)
        {
            foreach (Match match in LostMaterial.Matches(text))
            {
                string item = CleanItemName(match.Groups["item"].Value);
                int count;
                if (int.TryParse(match.Groups["count"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out count) == false)
                {
                    count = 1;
                }

                for (int index = 0; index < Math.Max(1, count); index++)
                    attempt.LostMaterials.Add(item);
            }
        }

        private static bool IsHighQuality(string text, string item)
        {
            return text.IndexOf("high-quality", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("high quality", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Regex.IsMatch(item, @"\s\+[1-3]$");
        }

        private static string CleanCrafter(string value)
        {
            return string.IsNullOrEmpty(value) ? "You" : value.Trim();
        }

        private static string CleanItemName(string value)
        {
            string item = (value ?? string.Empty).Trim().TrimEnd('.', '!').Trim();
            return item.Length == 0 ? "Unknown synthesis" : item;
        }

        private static string NormalizeCraftName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string craft = value.Trim().ToLowerInvariant();
            return char.ToUpperInvariant(craft[0]) + craft.Substring(1);
        }

        private static double ParseDouble(string value)
        {
            double result;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result
                : 0.0;
        }
    }
}
