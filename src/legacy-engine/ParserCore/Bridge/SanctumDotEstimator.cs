// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WaywardGamers.KParser.Database;
using WaywardGamers.KParser.Parsing;

namespace WaywardGamers.KParser.Bridge
{
    /// <summary>
    /// Builds a conservative damage-over-time estimate from effects that a
    /// supported server applies without emitting damage lines to the FFXI log.
    /// Standard rules are shared with Horizon; explicitly marked custom rules
    /// remain Sanctum-only. Estimates stay separate from observed damage.
    /// </summary>
    internal static class SanctumDotEstimator
    {
        private static readonly Regex ActionKeyCleanup =
            new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Dictionary<string, DotRule> Rules = CreateRules();

        internal static List<SanctumDotAggregate> Estimate(
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            bool parserRunning)
        {
            KPDatabaseDataSet.InteractionsRow[] eventRows = events.ToArray();
            Dictionary<int, KPDatabaseDataSet.BattlesRow> battleRows = battles
                .ToDictionary(battle => battle.BattleID);
            Dictionary<int, DateTime> battleEnds = battles.ToDictionary(
                battle => battle.BattleID,
                battle => GetBattleEnd(battle, eventRows, parserRunning));
            List<DotApplication> applications = new List<DotApplication>();
            Dictionary<string, DotApplication> active =
                new Dictionary<string, DotApplication>(StringComparer.Ordinal);

            foreach (KPDatabaseDataSet.InteractionsRow row in eventRows
                .Where(IsCandidateEvent)
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.InteractionID))
            {
                if (enemyIds.ContainsKey(row.BattleID) == false ||
                    row.TargetID != enemyIds[row.BattleID] ||
                    battleRows.ContainsKey(row.BattleID) == false)
                {
                    continue;
                }

                string actionName = GetActionName(row);
                string normalizedActionName = NormalizeActionName(actionName);
                string statePrefix = row.BattleID.ToString(CultureInfo.InvariantCulture) + "|" +
                                     row.TargetID.ToString(CultureInfo.InvariantCulture) + "|";
                if (TryCancelActiveEffect(
                    row,
                    normalizedActionName,
                    statePrefix,
                    active))
                {
                    continue;
                }

                DotRule rule;
                if (Rules.TryGetValue(normalizedActionName, out rule) == false ||
                    (rule.SanctumOnly && ServerCompatibility.IsSanctumXi == false) ||
                    IsSuccessfulApplication(row, rule) == false)
                {
                    continue;
                }

                bool usedCapturedStats;
                int power = ResolvePower(row, rule, out usedCapturedStats);
                if (power <= 0)
                    continue;

                bool usedCapturedDuration;
                int durationSeconds = ResolveDurationSeconds(
                    row,
                    rule,
                    out usedCapturedDuration);
                usedCapturedStats = usedCapturedStats || usedCapturedDuration;
                if (durationSeconds <= 0)
                    continue;

                DateTime start = AsUtc(row.Timestamp);
                DateTime battleEnd = battleEnds[row.BattleID];
                DateTime naturalEnd = start.AddSeconds(durationSeconds);
                DateTime end = naturalEnd < battleEnd ? naturalEnd : battleEnd;
                if (end <= start)
                    continue;

                string stateKey = statePrefix + rule.EffectKey;
                DotApplication existing;
                if (TryGetActive(active, stateKey, start, out existing))
                {
                    if (rule.OnlyIfEffectAbsent || CanOverwrite(rule, power, existing) == false)
                        continue;
                }

                // Dia and Bio only replace one another when the incoming tier is
                // stronger. A rule may also cancel an effect when its server script
                // explicitly removes that effect (Cold Wave removes Choke).
                bool blockedByOppositeTier = false;
                foreach (string cancelKey in rule.CancelEffectKeys)
                {
                    DotApplication opposite;
                    if (TryGetActive(active, statePrefix + cancelKey, start, out opposite) &&
                        rule.UsesOppositeTierGate &&
                        opposite.Rule.Tier >= rule.Tier)
                    {
                        blockedByOppositeTier = true;
                        break;
                    }
                }
                if (blockedByOppositeTier)
                    continue;

                if (existing != null && existing.End > start)
                {
                    existing.End = start;
                    active.Remove(stateKey);
                }

                foreach (string cancelKey in rule.CancelEffectKeys)
                {
                    string cancelStateKey = statePrefix + cancelKey;
                    DotApplication cancelled;
                    if (TryGetActive(active, cancelStateKey, start, out cancelled))
                    {
                        cancelled.End = start;
                        active.Remove(cancelStateKey);
                    }
                }

                DotApplication application = new DotApplication
                {
                    Actor = row.CombatantsRowByActorCombatantRelation,
                    EntityType = (EntityType)row.CombatantsRowByActorCombatantRelation.CombatantType,
                    ActionName = actionName,
                    ActionType = (ActionType)row.ActionType,
                    Rule = rule,
                    Power = power,
                    UsedCapturedStats = usedCapturedStats,
                    Start = start,
                    End = end
                };
                applications.Add(application);
                active[stateKey] = application;
            }

            return applications
                .Select(ToCompletedApplication)
                .Where(application => application.TickCount > 0 && application.Damage > 0)
                .GroupBy(application => new
                {
                    application.Actor.CombatantID,
                    Action = application.ActionName,
                    application.ActionType
                })
                .Select(group => new SanctumDotAggregate
                {
                    Actor = group.First().Actor,
                    EntityType = group.First().EntityType,
                    ActionName = group.Key.Action,
                    ActionType = group.Key.ActionType,
                    Damage = group.Sum(item => item.Damage),
                    ApplicationCount = group.LongCount(),
                    TickCount = group.Sum(item => item.TickCount),
                    ActiveSeconds = group.Sum(item => item.ActiveSeconds),
                    LowestPower = group.Min(item => (long)item.Power),
                    HighestPower = group.Max(item => (long)item.Power),
                    UsedCapturedStats = group.Any(item => item.UsedCapturedStats)
                })
                .ToList();
        }

        private static bool IsCandidateEvent(KPDatabaseDataSet.InteractionsRow row)
        {
            return row.Preparing == false &&
                   row.IsActorIDNull() == false &&
                   row.IsTargetIDNull() == false &&
                   row.IsBattleIDNull() == false &&
                   row.IsActionIDNull() == false &&
                   row.CombatantsRowByActorCombatantRelation != null;
        }

        private static bool TryCancelActiveEffect(
            KPDatabaseDataSet.InteractionsRow row,
            string normalizedActionName,
            string statePrefix,
            IDictionary<string, DotApplication> active)
        {
            if (normalizedActionName != "modus veritas" ||
                (FailedActionType)row.FailedActionType != FailedActionType.None ||
                (DefenseType)row.DefenseType == DefenseType.Absorb ||
                (DefenseType)row.DefenseType == DefenseType.Resist ||
                GetDirectDamage(row) <= 0)
            {
                return false;
            }

            string stateKey = statePrefix + "helix";
            DotApplication application;
            if (TryGetActive(active, stateKey, AsUtc(row.Timestamp), out application))
            {
                application.End = AsUtc(row.Timestamp);
                active.Remove(stateKey);
            }

            // Modus Veritas damage is already present in the parsed log. The
            // estimator only needs to stop the consumed Helix at this timestamp.
            return true;
        }

        private static bool IsSuccessfulApplication(
            KPDatabaseDataSet.InteractionsRow row,
            DotRule rule)
        {
            if ((FailedActionType)row.FailedActionType != FailedActionType.None ||
                (DefenseType)row.DefenseType == DefenseType.Absorb ||
                (DefenseType)row.DefenseType == DefenseType.Resist)
            {
                return false;
            }

            if (rule.ApplyWhenDamageLands ||
                (IsPetAction(row) && rule.PetApplyWhenDamageLands))
            {
                return GetDirectDamage(row) > 0 ||
                       HasSuccessfulEnfeeble(row) ||
                       rule.AllowCompletedZeroDamageApplication;
            }

            return HasSuccessfulEnfeeble(row);
        }

        private static bool HasSuccessfulEnfeeble(KPDatabaseDataSet.InteractionsRow row)
        {
            HarmType primary = (HarmType)row.HarmType;
            HarmType secondary = (HarmType)row.SecondHarmType;
            return primary == HarmType.Enfeeble || secondary == HarmType.Enfeeble;
        }

        private static int GetDirectDamage(KPDatabaseDataSet.InteractionsRow row)
        {
            HarmType harm = (HarmType)row.HarmType;
            return (harm == HarmType.Damage || harm == HarmType.Drain) &&
                   (DefenseType)row.DefenseType != DefenseType.Absorb
                ? Math.Max(0, row.Amount)
                : 0;
        }

        private static int ResolvePower(
            KPDatabaseDataSet.InteractionsRow row,
            DotRule rule,
            out bool usedCapturedStats)
        {
            usedCapturedStats = false;
            bool usePetVariant = IsPetAction(row) && rule.HasPetVariant;
            int estimatedPower = usePetVariant
                ? rule.PetEstimatedPower
                : rule.EstimatedPower;
            DotPowerFormula powerFormula = usePetVariant
                ? rule.PetPowerFormula
                : rule.PowerFormula;

            if (rule.UseDirectDamage)
                return GetDirectDamage(row);
            if (powerFormula == DotPowerFormula.DirectQuarter)
                return Math.Max(1, GetDirectDamage(row) / 4);
            if (powerFormula == DotPowerFormula.Fixed)
                return estimatedPower;

            KPDatabaseDataSet.CombatantsRow actor =
                row.CombatantsRowByActorCombatantRelation;
            SanctumPlayerStatProfile profile = GetProfileForActor(actor);
            if (profile == null)
                return estimatedPower;

            int value;
            switch (powerFormula)
            {
                case DotPowerFormula.ElementalDebuff:
                    value = GetElementalDebuffPower(profile.Intelligence);
                    break;

                case DotPowerFormula.PoisonOne:
                    if (profile.EnfeeblingSkill <= 0)
                        return estimatedPower;
                    value = profile.EnfeeblingSkill > 400
                        ? Math.Min((profile.EnfeeblingSkill - 225) / 5, 55)
                        : Math.Max(profile.EnfeeblingSkill / 25, 1);
                    break;

                case DotPowerFormula.PoisonTwo:
                    if (profile.EnfeeblingSkill <= 0)
                        return estimatedPower;
                    value = profile.EnfeeblingSkill > 400
                        ? (int)Math.Floor(
                            profile.EnfeeblingSkill * 49.0 / 183.0 - 55.0)
                        : Math.Max(profile.EnfeeblingSkill / 20, 4);
                    break;

                case DotPowerFormula.PoisonThree:
                    if (profile.EnfeeblingSkill <= 0)
                        return estimatedPower;
                    value = profile.EnfeeblingSkill / 10 + 1;
                    break;

                case DotPowerFormula.Dokumori:
                    if (profile.NinjutsuSkill <= 0)
                        return estimatedPower;
                    value = profile.NinjutsuSkill / 5 + 1;
                    break;

                case DotPowerFormula.BioOne:
                    if (profile.DarkSkill <= 0)
                        return estimatedPower;
                    value = Clamp((int)Math.Ceiling(profile.DarkSkill / 40.0), 1, 3);
                    break;

                case DotPowerFormula.BioTwo:
                    if (profile.DarkSkill <= 0)
                        return estimatedPower;
                    value = Clamp((profile.DarkSkill + 29) / 40, 3, 8);
                    break;

                case DotPowerFormula.BioThree:
                    if (profile.DarkSkill <= 0)
                        return estimatedPower;
                    if (profile.DarkSkill > 291)
                        value = 13 + (profile.DarkSkill - 291) / 27;
                    else if (profile.DarkSkill > 246)
                        value = 9 + (profile.DarkSkill - 246) / 11;
                    else
                        value = 5 + (profile.DarkSkill - 106) / 35;
                    value = Clamp(value, 5, 17);
                    break;

                case DotPowerFormula.BioFour:
                    if (profile.DarkSkill <= 0)
                        return estimatedPower;
                    value = 5 + profile.DarkSkill / 60;
                    break;

                case DotPowerFormula.BioFive:
                    if (profile.DarkSkill <= 0)
                        return estimatedPower;
                    value = 5 + profile.DarkSkill / 50;
                    break;

                case DotPowerFormula.SwordBurn:
                    if (profile.SwordSkill <= 0)
                        return estimatedPower;
                    value = GetWeaponBurnPower(profile.SwordSkill);
                    break;

                case DotPowerFormula.ArcheryBurn:
                    if (profile.ArcherySkill <= 0)
                        return estimatedPower;
                    value = GetWeaponBurnPower(profile.ArcherySkill);
                    break;

                case DotPowerFormula.MarksmanshipBurn:
                    if (profile.MarksmanshipSkill <= 0)
                        return estimatedPower;
                    value = GetWeaponBurnPower(profile.MarksmanshipSkill);
                    break;

                case DotPowerFormula.ClubShock:
                    if (profile.ClubSkill <= 0)
                        return estimatedPower;
                    value = profile.ClubSkill / 15;
                    break;

                case DotPowerFormula.DaggerPoison:
                    if (profile.DaggerSkill <= 0)
                        return estimatedPower;
                    value = Math.Min(15, 3 + profile.DaggerSkill / 20);
                    break;

                case DotPowerFormula.PetLeafDagger:
                    if (profile.MainJobLevel <= 0)
                        return estimatedPower;
                    value = Math.Max(1, profile.MainJobLevel / 10);
                    break;

                case DotPowerFormula.PetQueasyshroom:
                    if (profile.MainJobLevel <= 0)
                        return estimatedPower;
                    value = profile.MainJobLevel / 10 + 1;
                    break;

                case DotPowerFormula.PetToxicSpit:
                    if (profile.MainJobLevel <= 0)
                        return estimatedPower;
                    value = profile.MainJobLevel / 5 + 3;
                    break;

                default:
                    return estimatedPower;
            }

            usedCapturedStats = true;
            return Math.Max(1, value);
        }

        private static int ResolveDurationSeconds(
            KPDatabaseDataSet.InteractionsRow row,
            DotRule rule,
            out bool usedCapturedStats)
        {
            usedCapturedStats = false;
            if (IsPetAction(row) && rule.HasPetVariant)
                return rule.PetDurationSeconds;
            if (rule.DurationFormula != DotDurationFormula.Kaustra)
                return rule.DurationSeconds;

            SanctumPlayerStatProfile profile = GetProfileForActor(
                row.CombatantsRowByActorCombatantRelation);
            if (profile == null || profile.DarkSkill <= 0)
                return rule.DurationSeconds;

            usedCapturedStats = true;
            return Math.Max(3, (int)Math.Floor(3.0 * (1.0 + profile.DarkSkill / 11.0)));
        }

        private static int GetWeaponBurnPower(int skill)
        {
            return Math.Min(15, 3 + skill / 20);
        }

        private static bool IsPetAction(KPDatabaseDataSet.InteractionsRow row)
        {
            KPDatabaseDataSet.CombatantsRow actor =
                row.CombatantsRowByActorCombatantRelation;
            return actor != null &&
                   (EntityType)actor.CombatantType == EntityType.Pet;
        }

        private static SanctumPlayerStatProfile GetProfileForActor(
            KPDatabaseDataSet.CombatantsRow actor)
        {
            if (actor == null)
                return null;

            SanctumPlayerStatProfile profile =
                SanctumDotProfileStore.GetForActor(actor.CombatantName);
            if (profile != null || (EntityType)actor.CombatantType != EntityType.Pet)
                return profile;

            string currentPlayer = SanctumDotProfileStore.CurrentPlayerName;
            string petName;
            string ownerReference;
            if (SanctumPetName.TryParse(
                actor.CombatantName,
                out petName,
                out ownerReference) &&
                (string.Equals(
                    ownerReference,
                    currentPlayer,
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    ownerReference,
                    SanctumPetName.GetOwnerToken(currentPlayer),
                    StringComparison.OrdinalIgnoreCase)))
            {
                return SanctumDotProfileStore.GetForActor(currentPlayer);
            }

            string mappedOwner;
            return KParserBridgePetMappings.TryResolveOwner(
                actor.CombatantName,
                out mappedOwner)
                ? SanctumDotProfileStore.GetForActor(mappedOwner)
                : null;
        }

        private static int GetElementalDebuffPower(int intelligence)
        {
            if (intelligence > 150)
                return 5;
            if (intelligence > 100)
                return 4;
            if (intelligence > 70)
                return 3;
            if (intelligence > 40)
                return 2;
            return 1;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool TryGetActive(
            IDictionary<string, DotApplication> active,
            string key,
            DateTime at,
            out DotApplication application)
        {
            if (active.TryGetValue(key, out application) == false)
                return false;
            if (application.End > at)
                return true;

            active.Remove(key);
            application = null;
            return false;
        }

        private static bool CanOverwrite(DotRule incoming, int incomingPower, DotApplication existing)
        {
            if (incoming.Tier > 0 && existing.Rule.Tier > 0)
                return incoming.Tier >= existing.Rule.Tier;
            return incomingPower >= existing.Power;
        }

        private static CompletedDotApplication ToCompletedApplication(DotApplication application)
        {
            double activeSeconds = Math.Max(0.0, (application.End - application.Start).TotalSeconds);
            long ticks = (long)Math.Floor(activeSeconds / Math.Max(1, application.Rule.TickSeconds));
            return new CompletedDotApplication
            {
                Actor = application.Actor,
                EntityType = application.EntityType,
                ActionName = application.ActionName,
                ActionType = application.ActionType,
                Power = application.Power,
                UsedCapturedStats = application.UsedCapturedStats,
                TickCount = ticks,
                ActiveSeconds = activeSeconds,
                Damage = ticks * application.Power
            };
        }

        private static DateTime GetBattleEnd(
            KPDatabaseDataSet.BattlesRow battle,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            bool parserRunning)
        {
            if (battle.IsOver && battle.IsEndTimeNull() == false)
                return AsUtc(battle.EndTime);
            if (parserRunning)
                return DateTime.UtcNow;

            DateTime latest = events
                .Where(row => row.IsBattleIDNull() == false && row.BattleID == battle.BattleID)
                .Select(row => AsUtc(row.Timestamp))
                .DefaultIfEmpty(AsUtc(battle.StartTime))
                .Max();
            return latest;
        }

        private static string GetActionName(KPDatabaseDataSet.InteractionsRow row)
        {
            return row.ActionsRow == null || string.IsNullOrEmpty(row.ActionsRow.ActionName)
                ? ((ActionType)row.ActionType).ToString()
                : row.ActionsRow.ActionName;
        }

        private static string NormalizeActionName(string actionName)
        {
            return ActionKeyCleanup.Replace(
                    string.IsNullOrEmpty(actionName) ? string.Empty : actionName.ToLowerInvariant(),
                    " ")
                .Trim();
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static Dictionary<string, DotRule> CreateRules()
        {
            Dictionary<string, DotRule> rules =
                new Dictionary<string, DotRule>(StringComparer.Ordinal);

            Add(rules, Scaled(Fixed("poison", 11, 3, 90, 1), DotPowerFormula.PoisonOne), "Poison", "Poisonga");
            Add(rules, Scaled(Fixed("poison", 13, 3, 120, 2), DotPowerFormula.PoisonTwo), "Poison II");
            Add(rules, Scaled(Fixed("poison", 13, 3, 120, 1), DotPowerFormula.PoisonTwo), "Poisonga II");
            Add(rules, Scaled(Fixed("poison", 28, 3, 150, 3), DotPowerFormula.PoisonThree), "Poison III");
            Add(rules, Scaled(Fixed("poison", 28, 3, 150, 1), DotPowerFormula.PoisonThree), "Poisonga III");

            Add(rules, Scaled(Fixed("poison", 54, 3, 60, 1), DotPowerFormula.Dokumori), "Dokumori: Ichi", "Dokumori Ichi");
            Add(rules, Scaled(Fixed("poison", 54, 3, 120, 2), DotPowerFormula.Dokumori), "Dokumori: Ni", "Dokumori Ni");
            Add(rules, Scaled(Fixed("poison", 54, 3, 360, 3), DotPowerFormula.Dokumori), "Dokumori: San", "Dokumori San");

            Add(rules, ZeroDamageApplied(Opposed("dia", "bio", 1, 3, 60, 1)), "Dia", "Diaga");
            Add(rules, ZeroDamageApplied(Opposed("dia", "bio", 2, 3, 120, 3)), "Dia II", "Diaga II");
            Add(rules, ZeroDamageApplied(Opposed("dia", "bio", 3, 3, 180, 5)), "Dia III", "Diaga III");
            Add(rules, ZeroDamageApplied(Opposed("dia", "bio", 4, 3, 180, 7)), "Dia IV", "Diaga IV");
            Add(rules, ZeroDamageApplied(Opposed("dia", "bio", 5, 3, 180, 9)), "Dia V", "Diaga V");

            Add(rules, ZeroDamageApplied(Scaled(Opposed("bio", "dia", 3, 3, 60, 2), DotPowerFormula.BioOne)), "Bio");
            Add(rules, ZeroDamageApplied(Scaled(Opposed("bio", "dia", 7, 3, 120, 4), DotPowerFormula.BioTwo)), "Bio II");
            Add(rules, ZeroDamageApplied(Scaled(Opposed("bio", "dia", 11, 3, 180, 6), DotPowerFormula.BioThree)), "Bio III");
            Add(rules, ZeroDamageApplied(Scaled(Opposed("bio", "dia", 9, 3, 180, 8), DotPowerFormula.BioFour)), "Bio IV");
            Add(rules, ZeroDamageApplied(Scaled(Opposed("bio", "dia", 10, 3, 180, 10), DotPowerFormula.BioFive)), "Bio V");

            // Requiem potency can be raised by song gear and temporary effects.
            // The bridge cannot see those modifiers, so these are the exact
            // server base values and intentionally remain labelled estimates.
            Add(rules, Fixed("requiem", 1, 3, 64, 1), "Foe Requiem");
            Add(rules, Fixed("requiem", 2, 3, 80, 2), "Foe Requiem II");
            Add(rules, Fixed("requiem", 3, 3, 96, 3), "Foe Requiem III");
            Add(rules, Fixed("requiem", 4, 3, 112, 4), "Foe Requiem IV");
            Add(rules, Fixed("requiem", 5, 3, 128, 5), "Foe Requiem V");
            Add(rules, Fixed("requiem", 6, 3, 144, 6), "Foe Requiem VI");
            Add(rules, Fixed("requiem", 8, 3, 160, 7), "Foe Requiem VII");

            Add(rules, Elemental("burn", "frost", 4), "Burn");
            Add(rules, Elemental("frost", "choke", 4), "Frost");
            Add(rules, Elemental("choke", "rasp", 4), "Choke");
            Add(rules, Elemental("rasp", "shock", 4), "Rasp");
            Add(rules, Elemental("shock", "drown", 4), "Shock");
            Add(rules, Elemental("drown", "burn", 4), "Drown");

            string[] helixElements =
            {
                "Geohelix", "Hydrohelix", "Anemohelix", "Pyrohelix",
                "Cryohelix", "Ionohelix", "Noctohelix", "Luminohelix"
            };
            foreach (string helix in helixElements)
            {
                Add(rules, Direct("helix", 10, 90, 1), helix);
                Add(rules, Direct("helix", 10, 90, 2), helix + " II");
            }
            Add(
                rules,
                DamageApplied(Scaled(
                    DurationScaled(
                        Fixed("kaustra", 25, 3, 90, 1),
                        DotDurationFormula.Kaustra),
                    DotPowerFormula.DirectQuarter)),
                "Kaustra");

            Add(rules, Fixed("poison", 7, 3, 120, 0), "Venom Shell");
            Add(rules, Fixed("frost", 17, 3, 60, 0, "choke"), "Cold Wave");
            Add(rules, Fixed("poison", 7, 3, 45, 0), "Bad Breath");
            Add(rules, Fixed("poison", 3, 3, 60, 0), "Feather Storm");
            Add(rules, Fixed("poison", 18, 3, 180, 0), "Disseverment");
            Add(
                rules,
                PetVariant(
                    Fixed("poison", 3, 3, 180, 0),
                    8,
                    60,
                    DotPowerFormula.PetQueasyshroom),
                "Queasyshroom");
            Add(rules, Fixed("poison", 5, 3, 60, 0), "Poison Breath");

            // Sanctum adds persistent elemental damage to these weapon skills.
            // Horizon follows the ordinary LSB behavior and must not inherit it.
            Add(
                rules,
                SanctumOnly(DamageApplied(Scaled(
                    Fixed("burn", 15, 3, 45, 0, "frost"),
                    DotPowerFormula.SwordBurn))),
                "Burning Blade");
            Add(
                rules,
                SanctumOnly(DamageApplied(Scaled(
                    Fixed("burn", 15, 3, 30, 0, "frost"),
                    DotPowerFormula.MarksmanshipBurn))),
                "Hot Shot");
            Add(
                rules,
                SanctumOnly(DamageApplied(Scaled(
                    Fixed("burn", 15, 3, 30, 0, "frost"),
                    DotPowerFormula.ArcheryBurn))),
                "Flaming Arrow");
            Add(
                rules,
                SanctumOnly(DamageApplied(Scaled(
                    Fixed("shock", 17, 3, 60, 0, "drown"),
                    DotPowerFormula.ClubShock))),
                "Brainshaker");
            Add(
                rules,
                OnlyIfAbsent(DamageApplied(Fixed("poison", 1, 3, 90, 0))),
                "Wasp Sting");
            Add(
                rules,
                OnlyIfAbsent(DamageApplied(Scaled(
                    Fixed("poison", 15, 3, 90, 0),
                    DotPowerFormula.DaggerPoison))),
                "Viper Bite");
            Add(
                rules,
                OnlyIfAbsent(DamageApplied(Fixed("poison", 10, 3, 90, 0))),
                "Blade: Yu",
                "Blade Yu");

            Add(
                rules,
                DamageApplied(Scaled(
                    Fixed("poison", 7, 3, 90, 0),
                    DotPowerFormula.PetLeafDagger)),
                "Leaf Dagger");
            Add(
                rules,
                DamageApplied(Fixed("bio", 12, 3, 120, 0, "dia")),
                "Purulent Ooze");
            Add(
                rules,
                Scaled(
                    Fixed("poison", 18, 3, 180, 0),
                    DotPowerFormula.PetToxicSpit),
                "Toxic Spit");
            Add(rules, DamageApplied(Fixed("poison", 2, 3, 60, 0)), "Venom");
            Add(rules, Fixed("poison", 15, 3, 120, 0), "Venom Spray");

            DotRule poisonNails = DamageApplied(Fixed("poison", 1, 3, 60, 0));
            poisonNails.OnlyIfEffectAbsent = true;
            Add(rules, poisonNails, "Poison Nails");

            DotRule nightmare = Opposed("bio", "dia", 2, 3, 90, 11);
            Add(rules, nightmare, "Nightmare");

            return rules;
        }

        private static DotRule Fixed(
            string effectKey,
            int power,
            int tickSeconds,
            int durationSeconds,
            int tier,
            params string[] cancelKeys)
        {
            return new DotRule
            {
                EffectKey = effectKey,
                EstimatedPower = power,
                TickSeconds = tickSeconds,
                DurationSeconds = durationSeconds,
                Tier = tier,
                CancelEffectKeys = cancelKeys ?? new string[0]
            };
        }

        private static DotRule Elemental(
            string effectKey,
            string cancelKey,
            int power)
        {
            return Scaled(
                Fixed(effectKey, power, 3, 90, 1, cancelKey),
                DotPowerFormula.ElementalDebuff);
        }

        private static DotRule Opposed(
            string effectKey,
            string cancelKey,
            int power,
            int tickSeconds,
            int durationSeconds,
            int tier)
        {
            DotRule rule = Fixed(effectKey, power, tickSeconds, durationSeconds, tier, cancelKey);
            rule.UsesOppositeTierGate = true;
            return rule;
        }

        private static DotRule Direct(string effectKey, int tickSeconds, int durationSeconds, int tier)
        {
            DotRule rule = Fixed(effectKey, 0, tickSeconds, durationSeconds, tier);
            rule.UseDirectDamage = true;
            rule.ApplyWhenDamageLands = true;
            return rule;
        }

        private static DotRule DamageApplied(DotRule rule)
        {
            rule.ApplyWhenDamageLands = true;
            return rule;
        }

        private static DotRule ZeroDamageApplied(DotRule rule)
        {
            rule.ApplyWhenDamageLands = true;
            rule.AllowCompletedZeroDamageApplication = true;
            return rule;
        }

        private static DotRule Scaled(DotRule rule, DotPowerFormula formula)
        {
            rule.PowerFormula = formula;
            return rule;
        }

        private static DotRule DurationScaled(
            DotRule rule,
            DotDurationFormula formula)
        {
            rule.DurationFormula = formula;
            return rule;
        }

        private static DotRule OnlyIfAbsent(DotRule rule)
        {
            rule.OnlyIfEffectAbsent = true;
            return rule;
        }

        private static DotRule SanctumOnly(DotRule rule)
        {
            rule.SanctumOnly = true;
            return rule;
        }

        private static DotRule PetVariant(
            DotRule rule,
            int power,
            int durationSeconds,
            DotPowerFormula formula)
        {
            rule.HasPetVariant = true;
            rule.PetEstimatedPower = power;
            rule.PetDurationSeconds = durationSeconds;
            rule.PetPowerFormula = formula;
            rule.PetApplyWhenDamageLands = true;
            return rule;
        }

        private static void Add(
            IDictionary<string, DotRule> rules,
            DotRule rule,
            params string[] actionNames)
        {
            foreach (string actionName in actionNames)
                rules[NormalizeActionName(actionName)] = rule;
        }

        private sealed class DotRule
        {
            public string EffectKey { get; set; }
            public string[] CancelEffectKeys { get; set; }
            public int EstimatedPower { get; set; }
            public int TickSeconds { get; set; }
            public int DurationSeconds { get; set; }
            public int Tier { get; set; }
            public bool UseDirectDamage { get; set; }
            public bool ApplyWhenDamageLands { get; set; }
            public bool AllowCompletedZeroDamageApplication { get; set; }
            public bool UsesOppositeTierGate { get; set; }
            public bool OnlyIfEffectAbsent { get; set; }
            public DotPowerFormula PowerFormula { get; set; }
            public DotDurationFormula DurationFormula { get; set; }
            public bool HasPetVariant { get; set; }
            public int PetEstimatedPower { get; set; }
            public int PetDurationSeconds { get; set; }
            public DotPowerFormula PetPowerFormula { get; set; }
            public bool PetApplyWhenDamageLands { get; set; }
            public bool SanctumOnly { get; set; }
        }

        private enum DotPowerFormula
        {
            Fixed,
            ElementalDebuff,
            PoisonOne,
            PoisonTwo,
            PoisonThree,
            Dokumori,
            BioOne,
            BioTwo,
            BioThree,
            BioFour,
            BioFive,
            DirectQuarter,
            SwordBurn,
            ArcheryBurn,
            MarksmanshipBurn,
            ClubShock,
            DaggerPoison,
            PetLeafDagger,
            PetQueasyshroom,
            PetToxicSpit
        }

        private enum DotDurationFormula
        {
            Fixed,
            Kaustra
        }

        private sealed class DotApplication
        {
            public KPDatabaseDataSet.CombatantsRow Actor { get; set; }
            public EntityType EntityType { get; set; }
            public string ActionName { get; set; }
            public ActionType ActionType { get; set; }
            public DotRule Rule { get; set; }
            public int Power { get; set; }
            public bool UsedCapturedStats { get; set; }
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
        }

        private sealed class CompletedDotApplication
        {
            public KPDatabaseDataSet.CombatantsRow Actor { get; set; }
            public EntityType EntityType { get; set; }
            public string ActionName { get; set; }
            public ActionType ActionType { get; set; }
            public int Power { get; set; }
            public bool UsedCapturedStats { get; set; }
            public long TickCount { get; set; }
            public double ActiveSeconds { get; set; }
            public long Damage { get; set; }
        }
    }

    internal sealed class SanctumDotAggregate
    {
        public KPDatabaseDataSet.CombatantsRow Actor { get; set; }
        public EntityType EntityType { get; set; }
        public string ActionName { get; set; }
        public ActionType ActionType { get; set; }
        public long Damage { get; set; }
        public long ApplicationCount { get; set; }
        public long TickCount { get; set; }
        public double ActiveSeconds { get; set; }
        public long LowestPower { get; set; }
        public long HighestPower { get; set; }
        public bool UsedCapturedStats { get; set; }
    }
}
