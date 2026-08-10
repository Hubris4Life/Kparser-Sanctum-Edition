using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Infrastructure;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.ViewModels;

internal sealed class MainWindowViewModel : ObservableObject
{
    private static readonly Brush RunningBrush = CreateBrush(78, 194, 126);
    private static readonly Brush StoppedBrush = CreateBrush(218, 80, 87);

    private CombatantRow? selectedCombatant;
    private EncounterFilterOption? selectedEncounter;
    private ReportFilterOption? selectedCombatantScope;
    private ReportFilterOption? selectedDisplayMode;
    private ReportFilterOption? selectedGroupMode;
    private string selectedReport = "damageDealt";
    private string reportSearchText = string.Empty;
    private string refreshText = "Waiting for the first live snapshot";
    private string encounterTitle = "Damage Dealt — Running Total";
    private string encounterSubtitle = "Parser stopped — press Start to begin parsing";
    private string duration = "00:00";
    private string totalDamage = "0";
    private string allianceDps = "0.0";
    private string statusText = "Parser stopped · waiting for the bundled engine";
    private string eventStatus = "No combat events in this session";
    private string connectionBadge = "PARSER STOPPED";
    private string engineActionText = "Starting the bundled parsing engine…";
    private string primaryColumnLabel = "Damage";
    private string nameColumnLabel = "Combatant";
    private string secondaryColumnLabel = "Job";
    private string shareColumnLabel = "Share";
    private string rateColumnLabel = "DPS";
    private string detail1ColumnLabel = "Melee";
    private string detail2ColumnLabel = "Weapon skills";
    private string detail3ColumnLabel = "Magic";
    private string detail4ColumnLabel = "Other";
    private string summaryTotalLabel = "TOTAL DAMAGE";
    private string summaryRateLabel = "ALLIANCE DPS";
    private string totalRowLabel = "Alliance total";
    private string footerRightText = "Running total across all mob fights";
    private Brush parserStatusBrush = StoppedBrush;
    private bool hasReceivedLiveSnapshot;
    private bool engineConnected;
    private bool parserRunning;
    private bool engineCommandBusy;
    private bool updatingSelectors;
    private string? pendingEncounterKey;
    private double currentDurationSeconds;
    private string currentEncounterName = "All Encounters";
    private string currentEncounterScope = "all";
    private int currentFightCount;
    private int currentEventCount;
    private string currentEngineVersion = string.Empty;

    public MainWindowViewModel()
    {
        Encounters =
        [
            new EncounterFilterOption
            {
                Scope = "all",
                Label = "Running total — all mob fights"
            }
        ];

        CombatantScopes = [];
        ResetCombatantScopes("damageDealt");

        DisplayModes = [];
        GroupingModes = [];
        UpdateDisplayModes("damageDealt");

        Combatants = [];

        updatingSelectors = true;
        SelectedEncounter = Encounters[0];
        SelectedCombatantScope = CombatantScopes[0];
        SelectedDisplayMode = DisplayModes[0];
        updatingSelectors = false;
        SelectedCombatant = null;

        RefreshCommand = new DelegateCommand(RequestRefresh);
        StartParserCommand = new DelegateCommand(() => RequestEngineCommand("start"));
        StopParserCommand = new DelegateCommand(() => RequestEngineCommand("stop"));
        ResetParserCommand = new DelegateCommand(() => RequestEngineCommand("reset"));
        DetectMemoryCommand = new DelegateCommand(() => RequestEngineCommand("detect"));
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? ReportFilterChanged;
    public event EventHandler? ReportLayoutChanged;
    public event EventHandler<EngineCommandRequestedEventArgs>? EngineCommandRequested;

    public ObservableCollection<EncounterFilterOption> Encounters { get; }
    public ObservableCollection<ReportFilterOption> CombatantScopes { get; }
    public ObservableCollection<ReportFilterOption> DisplayModes { get; }
    public ObservableCollection<ReportFilterOption> GroupingModes { get; }
    public ObservableCollection<CombatantRow> Combatants { get; }
    public ICommand RefreshCommand { get; }
    public ICommand StartParserCommand { get; }
    public ICommand StopParserCommand { get; }
    public ICommand ResetParserCommand { get; }
    public ICommand DetectMemoryCommand { get; }

    public EncounterFilterOption? SelectedEncounter
    {
        get => selectedEncounter;
        set
        {
            if (SetProperty(ref selectedEncounter, value) && !updatingSelectors)
                ReportFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ReportFilterOption? SelectedCombatantScope
    {
        get => selectedCombatantScope;
        set
        {
            if (SetProperty(ref selectedCombatantScope, value) && !updatingSelectors)
                ReportFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ReportFilterOption? SelectedDisplayMode
    {
        get => selectedDisplayMode;
        set
        {
            var wasUpdating = updatingSelectors;
            if (SetProperty(ref selectedDisplayMode, value))
            {
                UpdateGroupingModes(selectedReport, value?.Key);
                RaisePropertyChanged(nameof(ShowDamageSourceFooter));
                RaisePropertyChanged(nameof(ShowCombatantFilter));
                if (!wasUpdating)
                    ReportFilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ReportFilterOption? SelectedGroupMode
    {
        get => selectedGroupMode;
        set
        {
            if (SetProperty(ref selectedGroupMode, value))
            {
                RaisePropertyChanged(nameof(IsActionGrouping));
                RaisePropertyChanged(nameof(CanSavePlayerSnapshot));
                RaisePropertyChanged(nameof(ShowDamageSourceFooter));
                if (!updatingSelectors)
                    ReportFilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string SelectedFilterScope => SelectedEncounter?.Scope ?? "all";
    public int SelectedFilterBattleId => SelectedEncounter?.BattleId ?? 0;
    public string? SelectedFilterMobName => SelectedEncounter?.MobName;
    public string SelectedEncounterKey => SelectedEncounter?.Key ?? "all:0:";
    public string SelectedReport => selectedReport;
    public string SelectedCombatantScopeKey => SelectedCombatantScope?.Key ?? "all";
    public string SelectedDisplayModeKey => SelectedDisplayMode?.Key ?? "summary";
    public string SelectedGroupModeKey => SelectedGroupMode?.Key ?? "player";
    public string ReportSearchText
    {
        get => reportSearchText;
        set
        {
            if (SetProperty(ref reportSearchText, value) && !updatingSelectors)
                ReportFilterChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool IsActionGrouping => SelectedGroupModeKey == "action";
    public bool ShowCombatantFilter => !IsChatSelected && !IsExperienceSelected &&
                                       !(IsLootSelected && SelectedDisplayModeKey == "helm");
    public bool ShowSearchFilter => IsChatSelected || IsLootSelected || IsCraftingSelected;
    public bool ShowTotalRow => !IsChatSelected;
    public bool ShowSelectedFooter => !IsChatSelected;
    public string EncounterFilterLabel => IsChatSelected
        ? "Speaker"
        : IsCraftingSelected ? "Crafting session" : "Fight view";
    public string CombatantFilterLabel => IsLootSelected
        ? "Recipients"
        : IsCraftingSelected ? "Crafters" : "Combatants";
    public string SearchFilterLabel => IsChatSelected
        ? "Search chat"
        : IsCraftingSelected ? "Search crafting" : "Search loot";
    public bool ShowDamageSourceFooter =>
        (IsDamageDealtSelected && !IsActionGrouping && SelectedDisplayModeKey != "dots") ||
        (IsFightsSelected && SelectedDisplayModeKey == "performance");

    public bool IsDamageDealtSelected
    {
        get => selectedReport == "damageDealt";
        set { if (value) SelectReport("damageDealt"); }
    }

    public bool IsDamageTakenSelected
    {
        get => selectedReport == "damageTaken";
        set { if (value) SelectReport("damageTaken"); }
    }

    public bool IsHealingSelected
    {
        get => selectedReport == "healing";
        set { if (value) SelectReport("healing"); }
    }

    public bool IsBuffsSelected
    {
        get => selectedReport == "buffs";
        set { if (value) SelectReport("buffs"); }
    }

    public bool IsDebuffsSelected
    {
        get => selectedReport == "debuffs";
        set { if (value) SelectReport("debuffs"); }
    }

    public bool IsDeathsSelected
    {
        get => selectedReport == "deaths";
        set { if (value) SelectReport("deaths"); }
    }

    public bool IsFightsSelected
    {
        get => selectedReport == "fights";
        set { if (value) SelectReport("fights"); }
    }

    public bool IsExperienceSelected
    {
        get => selectedReport == "experience";
        set { if (value) SelectReport("experience"); }
    }

    public bool IsChatSelected
    {
        get => selectedReport == "chat";
        set { if (value) SelectReport("chat"); }
    }

    public bool IsLootSelected
    {
        get => selectedReport == "loot";
        set { if (value) SelectReport("loot"); }
    }

    public bool IsCraftingSelected
    {
        get => selectedReport == "crafting";
        set { if (value) SelectReport("crafting"); }
    }

    public CombatantRow? SelectedCombatant
    {
        get => selectedCombatant;
        set
        {
            if (SetProperty(ref selectedCombatant, value))
            {
                RaisePropertyChanged(nameof(CanSavePlayerSnapshot));
                RaisePropertyChanged(nameof(CanCaptureDotStats));
            }
        }
    }

    public string RefreshText { get => refreshText; private set => SetProperty(ref refreshText, value); }
    public string EncounterTitle { get => encounterTitle; private set => SetProperty(ref encounterTitle, value); }
    public string EncounterSubtitle { get => encounterSubtitle; private set => SetProperty(ref encounterSubtitle, value); }
    public string Duration { get => duration; private set => SetProperty(ref duration, value); }
    public string TotalDamage { get => totalDamage; private set => SetProperty(ref totalDamage, value); }
    public string AllianceDps { get => allianceDps; private set => SetProperty(ref allianceDps, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string EventStatus { get => eventStatus; private set => SetProperty(ref eventStatus, value); }
    public string ConnectionBadge { get => connectionBadge; private set => SetProperty(ref connectionBadge, value); }
    public string EngineActionText { get => engineActionText; private set => SetProperty(ref engineActionText, value); }
    public string NameColumnLabel { get => nameColumnLabel; private set => SetProperty(ref nameColumnLabel, value); }
    public string SecondaryColumnLabel { get => secondaryColumnLabel; private set => SetProperty(ref secondaryColumnLabel, value); }
    public string PrimaryColumnLabel { get => primaryColumnLabel; private set => SetProperty(ref primaryColumnLabel, value); }
    public string ShareColumnLabel { get => shareColumnLabel; private set => SetProperty(ref shareColumnLabel, value); }
    public string RateColumnLabel { get => rateColumnLabel; private set => SetProperty(ref rateColumnLabel, value); }
    public string Detail1ColumnLabel { get => detail1ColumnLabel; private set => SetProperty(ref detail1ColumnLabel, value); }
    public string Detail2ColumnLabel { get => detail2ColumnLabel; private set => SetProperty(ref detail2ColumnLabel, value); }
    public string Detail3ColumnLabel { get => detail3ColumnLabel; private set => SetProperty(ref detail3ColumnLabel, value); }
    public string Detail4ColumnLabel { get => detail4ColumnLabel; private set => SetProperty(ref detail4ColumnLabel, value); }
    public string SummaryTotalLabel { get => summaryTotalLabel; private set => SetProperty(ref summaryTotalLabel, value); }
    public string SummaryRateLabel { get => summaryRateLabel; private set => SetProperty(ref summaryRateLabel, value); }
    public string TotalRowLabel { get => totalRowLabel; private set => SetProperty(ref totalRowLabel, value); }
    public string FooterRightText { get => footerRightText; private set => SetProperty(ref footerRightText, value); }

    public Brush ParserStatusBrush
    {
        get => parserStatusBrush;
        private set => SetProperty(ref parserStatusBrush, value);
    }

    public bool IsStartEnabled => engineConnected && !parserRunning && !engineCommandBusy;
    public bool IsStopEnabled => engineConnected && parserRunning && !engineCommandBusy;
    public bool IsResetEnabled => engineConnected && !engineCommandBusy;
    public bool IsDetectEnabled => engineConnected && !parserRunning && !engineCommandBusy;
    public bool CanCaptureDotStats => engineConnected && !engineCommandBusy &&
                                      SelectedCombatant is { CombatantType: "Player" };
    public bool CanSavePlayerSnapshot => hasReceivedLiveSnapshot &&
                                         selectedReport == "damageDealt" &&
                                         !IsActionGrouping &&
                                         SelectedCombatant is { CombatantType: "Player" };

    public double CurrentDurationSeconds => currentDurationSeconds;
    public string CurrentEncounterName => currentEncounterName;
    public string CurrentEncounterScope => currentEncounterScope;
    public int CurrentFightCount => currentFightCount;
    public int CurrentEventCount => currentEventCount;
    public string CurrentEngineVersion => currentEngineVersion;

    public void RestorePreferences(
        string report,
        string encounterKey,
        string combatantScope,
        string displayMode,
        string groupMode)
    {
        updatingSelectors = true;
        try
        {
            var restoredReport = report switch
            {
                "damageTaken" or "healing" or "buffs" or "debuffs" or "deaths" or "fights" or "chat" or "loot" or "crafting" => report,
                _ => "damageDealt"
            };

            if (selectedReport != restoredReport)
            {
                selectedReport = restoredReport;
                RaisePropertyChanged(nameof(SelectedReport));
                RaisePropertyChanged(nameof(IsDamageDealtSelected));
                RaisePropertyChanged(nameof(IsDamageTakenSelected));
                RaisePropertyChanged(nameof(IsHealingSelected));
                RaisePropertyChanged(nameof(IsBuffsSelected));
                RaisePropertyChanged(nameof(IsDebuffsSelected));
                RaisePropertyChanged(nameof(IsDeathsSelected));
                RaisePropertyChanged(nameof(IsFightsSelected));
                RaisePropertyChanged(nameof(IsChatSelected));
                RaisePropertyChanged(nameof(IsLootSelected));
                RaisePropertyChanged(nameof(IsCraftingSelected));
                ResetCombatantScopes(restoredReport);
                UpdateDisplayModes(restoredReport);
                updatingSelectors = true;
            }

            SelectedCombatantScope = CombatantScopes.FirstOrDefault(option => option.Key == combatantScope)
                                     ?? CombatantScopes[0];
            SelectedDisplayMode = DisplayModes.FirstOrDefault(option => option.Key == displayMode)
                                  ?? DisplayModes[0];
            SelectedGroupMode = GroupingModes.FirstOrDefault(option => option.Key == groupMode)
                                ?? GroupingModes[0];
            pendingEncounterKey = string.IsNullOrWhiteSpace(encounterKey)
                ? "all:0:"
                : encounterKey;
        }
        finally
        {
            updatingSelectors = false;
        }
    }

    public void ApplySnapshot(BridgeSnapshot snapshot)
    {
        hasReceivedLiveSnapshot = true;
        engineConnected = true;
        engineCommandBusy = false;
        currentEngineVersion = snapshot.EngineVersion;
        SetParserState(snapshot.ParserRunning);
        ApplyEncounterFilters(snapshot.Filters);
        ApplyCombatantFilters(snapshot.CombatantFilters);
        ApplyColumnMetadata(snapshot.Columns);

        var generated = ParseUtc(snapshot.GeneratedUtc).ToLocalTime();
        RefreshText = "Live snapshot · updated " + generated.ToString("h:mm:ss tt");

        var parseMode = string.IsNullOrWhiteSpace(snapshot.ParseMode) ? string.Empty : " · " + snapshot.ParseMode;
        StatusText = snapshot.ParserRunning
            ? $"Parser active{parseMode} · bundled KParser engine {snapshot.EngineVersion}"
            : $"Parser stopped{parseMode} · bundled KParser engine {snapshot.EngineVersion}";

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            StatusText += " · " + snapshot.Error;
            return;
        }

        if (snapshot.Encounter is null)
        {
            ApplyNoEncounter(snapshot.DatabaseOpen, snapshot.ParserRunning);
            return;
        }

        var encounter = snapshot.Encounter;
        var localStart = ParseUtc(encounter.StartUtc).ToLocalTime();
        var displayDurationSeconds = encounter.DurationSeconds;
        if (encounter.IsActive && snapshot.ParserRunning)
            displayDurationSeconds += Math.Max(0, (DateTime.UtcNow - generated.ToUniversalTime()).TotalSeconds);
        var durationText = FormatDuration(displayDurationSeconds);
        var usesDurationRate = snapshot.Report == "damageDealt" ||
                               snapshot.Report == "damageTaken" ||
                               (snapshot.Report == "healing" && snapshot.DisplayMode != "status") ||
                               snapshot.Report == "fights";
        var usesDurationRowRate = snapshot.Report != "fights" &&
                                  usesDurationRate && snapshot.GroupMode != "action";
        var previousSelection = SelectedCombatant?.Key;
        var fightWord = encounter.FightCount == 1 ? "fight" : "fights";
        var reportTitle = snapshot.Report == "fights"
            ? snapshot.DisplayMode == "performance" ? "Player Performance" : "Fight History"
            : snapshot.Report == "damageDealt" && snapshot.DisplayMode == "dots"
                ? "Calculated Damage over Time"
                : GetReportTitle(snapshot.Report);

        switch (encounter.Scope)
        {
            case "chat":
                EncounterTitle = "Chat — All Speakers";
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active · showing the newest {snapshot.Combatants.Count:N0} of {encounter.EventCount:N0} matching messages"
                    : $"Parser stopped · showing the newest {snapshot.Combatants.Count:N0} of {encounter.EventCount:N0} matching messages";
                FooterRightText = "Newest messages are listed first";
                break;

            case "speaker":
                EncounterTitle = "Chat — " + encounter.Name;
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active · filtered to {encounter.Name} · {encounter.EventCount:N0} matching messages"
                    : $"Parser stopped · filtered to {encounter.Name} · {encounter.EventCount:N0} matching messages";
                FooterRightText = "Speaker filter: " + encounter.Name;
                break;

            case "crafting":
                EncounterTitle = snapshot.DisplayMode == "mine"
                    ? "Crafting — My Sessions"
                    : "Crafting — All Sessions";
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active · {encounter.EventCount:N0} recorded crafting attempts"
                    : $"Parser stopped · {encounter.EventCount:N0} recorded crafting attempts";
                FooterRightText = snapshot.DisplayMode == "mine"
                    ? "Local player's recorded crafting sessions"
                    : "All recorded crafting sessions";
                break;

            case "craftingSession":
                EncounterTitle = "Crafting — " + encounter.Name;
                EncounterSubtitle = $"Crafting session · started {localStart:g} · {encounter.EventCount:N0} attempts";
                FooterRightText = "Selected crafting session";
                break;

            case "battle":
                EncounterTitle = reportTitle + " — Fight History: " + encounter.Name;
                EncounterSubtitle = encounter.IsActive && snapshot.ParserRunning
                    ? $"Parser active · selected fight is still in progress · started {localStart:g}"
                    : $"Historical fight · started {localStart:g} · duration {durationText}";
                FooterRightText = "Selected historical fight";
                break;

            case "current":
                EncounterTitle = reportTitle + " — Current Fight: " + encounter.Name;
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active · showing the latest fight only · started {localStart:g}"
                    : $"Parser stopped · showing the latest fight only · started {localStart:g}";
                FooterRightText = "Latest fight only";
                break;

            case "mob":
                EncounterTitle = reportTitle + " — " + encounter.Name;
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active and waiting for mob data · filtered total across {encounter.FightCount:N0} {fightWord}"
                    : $"Parser stopped · filtered total across {encounter.FightCount:N0} {fightWord}";
                FooterRightText = "Filtered to " + encounter.Name;
                break;

            default:
                EncounterTitle = reportTitle + " — Running Total";
                EncounterSubtitle = snapshot.ParserRunning
                    ? $"Parser active and waiting for mob data · running total across {encounter.FightCount:N0} {fightWord}"
                    : $"Parser stopped · running total across {encounter.FightCount:N0} {fightWord}";
                FooterRightText = "Running total across all mob fights";
                break;
        }

        if (snapshot.Report == "damageDealt" && snapshot.DisplayMode == "dots")
        {
            EncounterSubtitle += " - server-rule estimate";
            FooterRightText = "Calculated DoT is separate from observed damage totals";
        }

        Duration = durationText;
        currentDurationSeconds = displayDurationSeconds;
        currentEncounterName = encounter.Name;
        currentEncounterScope = encounter.Scope;
        currentFightCount = encounter.FightCount;
        currentEventCount = encounter.EventCount;
        TotalDamage = encounter.TotalDamage.ToString("N0");
        AllianceDps = (usesDurationRate
                ? encounter.TotalDamage / Math.Max(1.0, displayDurationSeconds)
                : encounter.AllianceDps)
            .ToString("N1") + snapshot.Columns.RateSuffix;
        TotalRowLabel = snapshot.Report == "damageDealt" && snapshot.DisplayMode == "dots"
            ? "Calculated DoT total"
            : snapshot.Report == "chat"
                ? "Visible messages"
            : snapshot.Report == "loot"
                ? "Loot total"
            : snapshot.Report == "crafting"
                ? "Crafting attempts"
            : snapshot.Report == "fights" && snapshot.DisplayMode == "history"
                ? "Fight history total"
            : snapshot.Report == "fights"
                ? "Player performance total"
                : snapshot.CombatantScope switch
                {
                    "party" => "Party total",
                    "players" => "Player total",
                    "pets" => "Pet total",
                    _ => "Alliance total"
                };
        EventStatus = snapshot.Report == "chat"
            ? $"{encounter.EventCount:N0} matching messages · {encounter.FightCount:N0} speakers · newest 500 maximum"
            : snapshot.Report == "loot"
                ? $"{snapshot.Combatants.Count:N0} loot rows · {encounter.FightCount:N0} {fightWord}"
            : snapshot.Report == "crafting"
                ? $"{encounter.EventCount:N0} attempts · {encounter.FightCount:N0} sessions · success rate {encounter.AllianceDps:N1}%"
                : $"{encounter.EventCount:N0} combat events · {encounter.FightCount:N0} {fightWord}" +
                  (snapshot.Report == "damageDealt" && snapshot.DisplayMode == "dots"
                      ? " · calculated from effect applications"
                      : string.Empty);

        var updatedCombatants = snapshot.Combatants
            .OrderBy(row => row.Rank)
            .Select(combatant => new CombatantRow
            {
                Key = string.IsNullOrWhiteSpace(combatant.Key) ? combatant.Name : combatant.Key,
                Rank = combatant.Rank,
                Name = combatant.Name,
                CombatantType = combatant.CombatantType,
                IsLocalPlayer = combatant.IsLocalPlayer,
                IsActionDetail = snapshot.GroupMode == "action",
                Job = string.IsNullOrWhiteSpace(combatant.Job) ? "—" : combatant.Job,
                Damage = combatant.Damage,
                Share = combatant.SharePercent,
                Dps = usesDurationRowRate
                    ? combatant.Damage / Math.Max(1.0, displayDurationSeconds)
                    : combatant.Dps,
                Melee = combatant.Melee,
                WeaponSkills = combatant.WeaponSkills,
                Magic = combatant.Magic,
                Other = combatant.Other,
                MeleeDamage = combatant.MeleeDamage,
                WeaponSkillDamage = combatant.WeaponSkillDamage,
                MagicDamage = combatant.MagicDamage,
                Ranged = combatant.Ranged,
                Abilities = combatant.Abilities,
                Skillchains = combatant.Skillchains,
                AdditionalEffects = combatant.AdditionalEffects,
                Counters = combatant.Counters,
                Retaliation = combatant.Retaliation,
                Spikes = combatant.Spikes,
                PhysicalAttempts = combatant.PhysicalAttempts,
                PhysicalHits = combatant.PhysicalHits,
                PhysicalMisses = combatant.PhysicalMisses,
                CriticalHits = combatant.CriticalHits,
                PrimaryText = combatant.PrimaryText,
                ShareText = combatant.ShareText,
                RateText = combatant.RateText,
                Detail1Text = combatant.Detail1Text,
                Detail2Text = combatant.Detail2Text,
                Detail3Text = combatant.Detail3Text,
                Detail4Text = combatant.Detail4Text,
                RateSuffix = snapshot.Columns.RateSuffix,
                TopAction = combatant.TopAction,
                Accuracy = combatant.Accuracy,
                CriticalRate = combatant.CriticalRate
            })
            .ToArray();

        SynchronizeCombatants(Combatants, updatedCombatants);

        SelectedCombatant = Combatants.FirstOrDefault(row => row.Key == previousSelection)
                            ?? Combatants.FirstOrDefault();
    }

    public void SetDisconnected()
    {
        engineConnected = false;
        engineCommandBusy = false;
        parserRunning = false;
        ParserStatusBrush = StoppedBrush;
        ConnectionBadge = "ENGINE DISCONNECTED";
        RaiseControlStateChanged();

        if (hasReceivedLiveSnapshot)
        {
            StatusText = "Parser connection lost · showing the last live snapshot";
            EncounterSubtitle = "Parser disconnected · last received totals remain visible";
            EventStatus = "Last received data retained";
        }
        else
        {
            StatusText = "Parser stopped · waiting for the bundled engine";
            EncounterSubtitle = "Parser stopped — waiting for the bundled engine";
        }
    }

    public void SetEngineReady(bool startedBundledEngine)
    {
        engineConnected = true;
        RaiseControlStateChanged();
        EngineActionText = startedBundledEngine
            ? "Bundled engine ready · press Start to begin parsing"
            : "Connected to an existing KParser engine";
    }

    public void SetEngineLaunchFailed(string message)
    {
        engineConnected = false;
        engineCommandBusy = false;
        parserRunning = false;
        ParserStatusBrush = StoppedBrush;
        EngineActionText = message;
        StatusText = "Parser stopped · bundled engine unavailable";
        EncounterSubtitle = "Parser stopped — the bundled engine is unavailable";
        ConnectionBadge = "ENGINE NOT FOUND";
        RaiseControlStateChanged();
    }

    public void SetEngineCommandBusy(string command)
    {
        engineCommandBusy = true;
        RaiseControlStateChanged();

        switch (command)
        {
            case "start":
                ConnectionBadge = "STARTING PARSER…";
                EngineActionText = "Starting the parser and opening its combat database…";
                EncounterSubtitle = "Starting parser…";
                break;
            case "stop":
                ConnectionBadge = "STOPPING PARSER…";
                EngineActionText = "Stopping the parser safely…";
                break;
            case "reset":
                ConnectionBadge = "RESETTING PARSER…";
                EngineActionText = "Archiving the current parse and starting a fresh one…";
                break;
            case "detect":
                EngineActionText = "Scanning and validating FFXI chat memory…";
                break;
            case "capturestats":
                EngineActionText = "Reading and validating your current FFXI stats…";
                break;
        }
    }

    public void ApplyCommandResult(BridgeCommandResult result)
    {
        engineCommandBusy = false;
        if (result.Success)
            SetParserState(result.ParserRunning);
        else
            RaiseControlStateChanged();

        EngineActionText = result.Success
            ? result.Message
            : "Could not " + result.Command + ": " + result.Message;
    }

    public void SetShuttingDown()
    {
        engineCommandBusy = true;
        ConnectionBadge = "CLOSING PARSER…";
        EngineActionText = "Closing the bundled parsing engine safely…";
        RaiseControlStateChanged();
    }

    public void SetUserNotice(string message)
    {
        EngineActionText = message;
    }

    private void SelectReport(string report)
    {
        if (selectedReport == report)
            return;

        selectedReport = report;
        if (reportSearchText.Length > 0)
        {
            reportSearchText = string.Empty;
            RaisePropertyChanged(nameof(ReportSearchText));
        }
        RaisePropertyChanged(nameof(SelectedReport));
        RaisePropertyChanged(nameof(IsDamageDealtSelected));
        RaisePropertyChanged(nameof(IsDamageTakenSelected));
        RaisePropertyChanged(nameof(IsHealingSelected));
        RaisePropertyChanged(nameof(IsBuffsSelected));
        RaisePropertyChanged(nameof(IsDebuffsSelected));
        RaisePropertyChanged(nameof(IsDeathsSelected));
        RaisePropertyChanged(nameof(IsFightsSelected));
        RaisePropertyChanged(nameof(IsExperienceSelected));
        RaisePropertyChanged(nameof(IsChatSelected));
        RaisePropertyChanged(nameof(IsLootSelected));
        RaisePropertyChanged(nameof(IsCraftingSelected));
        RaisePropertyChanged(nameof(ShowCombatantFilter));
        RaisePropertyChanged(nameof(ShowSearchFilter));
        RaisePropertyChanged(nameof(ShowTotalRow));
        RaisePropertyChanged(nameof(ShowSelectedFooter));
        RaisePropertyChanged(nameof(EncounterFilterLabel));
        RaisePropertyChanged(nameof(CombatantFilterLabel));
        RaisePropertyChanged(nameof(SearchFilterLabel));
        RaisePropertyChanged(nameof(CanSavePlayerSnapshot));
        RaisePropertyChanged(nameof(ShowDamageSourceFooter));
        ResetCombatantScopes(report);
        UpdateDisplayModes(report);

        if (!updatingSelectors)
            ReportFilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplayModes(string report)
    {
        var wasUpdating = updatingSelectors;
        updatingSelectors = true;
        try
        {
            DisplayModes.Clear();
            switch (report)
            {
                case "damageTaken":
                    DisplayModes.Add(new ReportFilterOption { Key = "sources", Label = "Damage sources" });
                    DisplayModes.Add(new ReportFilterOption { Key = "melee", Label = "Melee" });
                    DisplayModes.Add(new ReportFilterOption { Key = "ranged", Label = "Ranged" });
                    DisplayModes.Add(new ReportFilterOption { Key = "magic", Label = "Magic" });
                    DisplayModes.Add(new ReportFilterOption { Key = "other", Label = "Other damage" });
                    DisplayModes.Add(new ReportFilterOption { Key = "defense", Label = "Defense details" });
                    DisplayModes.Add(new ReportFilterOption { Key = "buffperformance", Label = "Performance with defensive buffs" });
                    break;
                case "healing":
                    DisplayModes.Add(new ReportFilterOption { Key = "summary", Label = "Summary" });
                    DisplayModes.Add(new ReportFilterOption { Key = "spells", Label = "Magic healing" });
                    DisplayModes.Add(new ReportFilterOption { Key = "abilities", Label = "Ability healing" });
                    DisplayModes.Add(new ReportFilterOption { Key = "cures", Label = "Cure details" });
                    DisplayModes.Add(new ReportFilterOption { Key = "status", Label = "Status removal" });
                    DisplayModes.Add(new ReportFilterOption { Key = "recipients", Label = "Healing received by player" });
                    DisplayModes.Add(new ReportFilterOption { Key = "recovery", Label = "HP / MP / TP recovery" });
                    DisplayModes.Add(new ReportFilterOption { Key = "efficiency", Label = "Observed healing efficiency" });
                    break;
                case "buffs":
                    DisplayModes.Add(new ReportFilterOption { Key = "used", Label = "Buffs used" });
                    DisplayModes.Add(new ReportFilterOption { Key = "received", Label = "Buffs received" });
                    DisplayModes.Add(new ReportFilterOption { Key = "uptime", Label = "Buff duration / uptime" });
                    DisplayModes.Add(new ReportFilterOption { Key = "performance", Label = "Performance with buffs" });
                    DisplayModes.Add(new ReportFilterOption { Key = "corsair", Label = "Corsair roll statistics" });
                    break;
                case "debuffs":
                    DisplayModes.Add(new ReportFilterOption { Key = "summary", Label = "Success breakdown" });
                    DisplayModes.Add(new ReportFilterOption { Key = "magic", Label = "Magic debuffs" });
                    DisplayModes.Add(new ReportFilterOption { Key = "abilities", Label = "Ability debuffs" });
                    break;
                case "deaths":
                    DisplayModes.Add(new ReportFilterOption { Key = "summary", Label = "Death summary" });
                    break;
                case "fights":
                    DisplayModes.Add(new ReportFilterOption { Key = "history", Label = "Fight history" });
                    DisplayModes.Add(new ReportFilterOption { Key = "performance", Label = "Player performance" });
                    break;
                case "experience":
                    DisplayModes.Add(new ReportFilterOption { Key = "mobs", Label = "EXP by enemy" });
                    DisplayModes.Add(new ReportFilterOption { Key = "history", Label = "EXP fight history" });
                    DisplayModes.Add(new ReportFilterOption { Key = "chains", Label = "Chain distribution" });
                    DisplayModes.Add(new ReportFilterOption { Key = "difficulty", Label = "EXP by difficulty" });
                    break;
                case "chat":
                    DisplayModes.Add(new ReportFilterOption { Key = "all", Label = "All channels" });
                    DisplayModes.Add(new ReportFilterOption { Key = "say", Label = "Say" });
                    DisplayModes.Add(new ReportFilterOption { Key = "shout", Label = "Shout / Yell" });
                    DisplayModes.Add(new ReportFilterOption { Key = "party", Label = "Party" });
                    DisplayModes.Add(new ReportFilterOption { Key = "linkshell", Label = "Linkshell" });
                    DisplayModes.Add(new ReportFilterOption { Key = "tell", Label = "Tell" });
                    DisplayModes.Add(new ReportFilterOption { Key = "emote", Label = "Emote" });
                    DisplayModes.Add(new ReportFilterOption { Key = "npc", Label = "NPC" });
                    DisplayModes.Add(new ReportFilterOption { Key = "echo", Label = "Echo" });
                    DisplayModes.Add(new ReportFilterOption { Key = "arena", Label = "Arena" });
                    break;
                case "loot":
                    DisplayModes.Add(new ReportFilterOption { Key = "summary", Label = "Item summary" });
                    DisplayModes.Add(new ReportFilterOption { Key = "distribution", Label = "Recipient distribution" });
                    DisplayModes.Add(new ReportFilterOption { Key = "rates", Label = "Drop rates" });
                    DisplayModes.Add(new ReportFilterOption { Key = "treasurehunter", Label = "Drop rates by Treasure Hunter" });
                    DisplayModes.Add(new ReportFilterOption { Key = "helm", Label = "HELM activity" });
                    break;
                case "crafting":
                    DisplayModes.Add(new ReportFilterOption { Key = "summary", Label = "Recipe summary" });
                    DisplayModes.Add(new ReportFilterOption { Key = "mine", Label = "My crafting" });
                    DisplayModes.Add(new ReportFilterOption { Key = "history", Label = "Attempt history" });
                    DisplayModes.Add(new ReportFilterOption { Key = "skillups", Label = "Skill-up tracking" });
                    DisplayModes.Add(new ReportFilterOption { Key = "materials", Label = "Lost materials" });
                    break;
                default:
                    DisplayModes.Add(new ReportFilterOption { Key = "sources", Label = "Damage sources" });
                    DisplayModes.Add(new ReportFilterOption { Key = "melee", Label = "Melee" });
                    DisplayModes.Add(new ReportFilterOption { Key = "ranged", Label = "Ranged" });
                    DisplayModes.Add(new ReportFilterOption { Key = "weaponskills", Label = "Weapon skills" });
                    DisplayModes.Add(new ReportFilterOption { Key = "abilities", Label = "Abilities" });
                    DisplayModes.Add(new ReportFilterOption { Key = "magic", Label = "Magic" });
                    DisplayModes.Add(new ReportFilterOption { Key = "dots", Label = "Damage over time (calculated)" });
                    DisplayModes.Add(new ReportFilterOption { Key = "skillchains", Label = "Skillchains" });
                    DisplayModes.Add(new ReportFilterOption { Key = "additional", Label = "Additional effects" });
                    DisplayModes.Add(new ReportFilterOption { Key = "reactive", Label = "Reactive damage" });
                    DisplayModes.Add(new ReportFilterOption { Key = "accuracy", Label = "Accuracy" });
                    DisplayModes.Add(new ReportFilterOption { Key = "multiattacks", Label = "Multi-attack rounds (inferred)" });
                    break;
            }

            SelectedDisplayMode = DisplayModes.FirstOrDefault();
        }
        finally
        {
            updatingSelectors = wasUpdating;
        }
    }

    private void UpdateGroupingModes(string report, string? displayMode)
    {
        var wasUpdating = updatingSelectors;
        var previousKey = SelectedGroupMode?.Key ?? "player";
        updatingSelectors = true;
        try
        {
            GroupingModes.Clear();
            if (report == "buffs" && displayMode == "uptime")
            {
                GroupingModes.Add(new ReportFilterOption { Key = "action", Label = "By buff" });
                SelectedGroupMode = GroupingModes[0];
                return;
            }

            GroupingModes.Add(new ReportFilterOption
            {
                Key = "player",
                Label = report == "fights" || report == "experience" || report == "chat" || report == "loot" || report == "crafting"
                    ? "Report rows"
                    : "By player"
            });
            if (SupportsActionGrouping(report, displayMode))
                GroupingModes.Add(new ReportFilterOption { Key = "action", Label = "By action" });

            SelectedGroupMode = GroupingModes.FirstOrDefault(option => option.Key == previousKey)
                                ?? GroupingModes[0];
        }
        finally
        {
            updatingSelectors = wasUpdating;
        }
    }

    private static bool SupportsActionGrouping(string report, string? displayMode)
    {
        if (report == "damageDealt")
        {
            return displayMode is "melee" or "ranged" or "weaponskills" or
                   "abilities" or "magic" or "dots" or "skillchains" or "additional" or "reactive";
        }

        if (report == "damageTaken")
            return displayMode is "melee" or "ranged" or "magic" or "other";
        if (report == "healing")
            return displayMode is "spells" or "abilities" or "cures" or "status";
        return report is "buffs" or "debuffs";
    }

    private void ApplyEncounterFilters(IReadOnlyCollection<BridgeEncounterFilter> filters)
    {
        if (filters.Count == 0)
            return;

        var selectedKey = pendingEncounterKey ?? SelectedEncounter?.Key ?? "all:0:";
        var updatedFilters = filters.Select(filter => new EncounterFilterOption
        {
            Scope = string.IsNullOrWhiteSpace(filter.Scope) ? "all" : filter.Scope,
            BattleId = filter.BattleId,
            MobName = string.IsNullOrWhiteSpace(filter.MobName) ? null : filter.MobName,
            Label = filter.Label
        }).ToArray();

        if (Encounters.Count == updatedFilters.Length &&
            Encounters.Zip(updatedFilters).All(pair =>
                pair.First.Key == pair.Second.Key && pair.First.Label == pair.Second.Label))
        {
            pendingEncounterKey = null;
            return;
        }

        updatingSelectors = true;
        try
        {
            Encounters.Clear();
            foreach (var filter in updatedFilters)
                Encounters.Add(filter);

            SelectedEncounter = Encounters.FirstOrDefault(option => option.Key == selectedKey)
                                ?? Encounters.FirstOrDefault();
            pendingEncounterKey = null;
        }
        finally
        {
            updatingSelectors = false;
        }
    }

    private void ApplyCombatantFilters(IReadOnlyCollection<BridgeCombatantFilter> filters)
    {
        if (filters.Count == 0)
            return;

        var selectedKey = SelectedCombatantScope?.Key ?? "all";
        var updatedFilters = filters.Select(filter => new ReportFilterOption
        {
            Key = filter.Key,
            Label = filter.Label
        }).ToArray();

        if (CombatantScopes.Count == updatedFilters.Length &&
            CombatantScopes.Zip(updatedFilters).All(pair =>
                pair.First.Key == pair.Second.Key && pair.First.Label == pair.Second.Label))
        {
            return;
        }

        updatingSelectors = true;
        try
        {
            CombatantScopes.Clear();
            foreach (var filter in updatedFilters)
                CombatantScopes.Add(filter);

            SelectedCombatantScope = CombatantScopes.FirstOrDefault(option => option.Key == selectedKey)
                                     ?? CombatantScopes.FirstOrDefault();
        }
        finally
        {
            updatingSelectors = false;
        }
    }

    private void ResetCombatantScopes(string report)
    {
        var previousKey = SelectedCombatantScope?.Key ?? "all";
        var wasUpdating = updatingSelectors;
        updatingSelectors = true;
        try
        {
            CombatantScopes.Clear();
            if (report == "loot")
            {
                CombatantScopes.Add(new ReportFilterOption
                {
                    Key = "all",
                    Label = "All recipients"
                });
            }
            else if (report == "crafting")
            {
                CombatantScopes.Add(new ReportFilterOption
                {
                    Key = "all",
                    Label = "All crafters"
                });
            }
            else
            {
                CombatantScopes.Add(new ReportFilterOption
                {
                    Key = "all",
                    Label = report == "damageDealt" ? "Alliance (pets attributed)" : "Entire alliance"
                });
                CombatantScopes.Add(new ReportFilterOption
                {
                    Key = "party",
                    Label = report == "damageDealt" ? "Party (pets attributed)" : "Party only"
                });
                CombatantScopes.Add(new ReportFilterOption
                {
                    Key = "players",
                    Label = report == "damageDealt" ? "Player damage only" : "Players only"
                });
                if (report == "damageDealt")
                {
                    CombatantScopes.Add(new ReportFilterOption
                    {
                        Key = "pets",
                        Label = "Pet damage only"
                    });
                }
            }

            SelectedCombatantScope = CombatantScopes.FirstOrDefault(option => option.Key == previousKey)
                                     ?? CombatantScopes[0];
        }
        finally
        {
            updatingSelectors = wasUpdating;
        }
    }

    private static void SynchronizeCombatants(
        ObservableCollection<CombatantRow> current,
        IReadOnlyList<CombatantRow> updated)
    {
        for (var index = 0; index < updated.Count; index++)
        {
            var incoming = updated[index];
            if (index >= current.Count)
            {
                current.Add(incoming);
                continue;
            }

            if (current[index].Key != incoming.Key)
            {
                var existingIndex = -1;
                for (var search = index + 1; search < current.Count; search++)
                {
                    if (current[search].Key == incoming.Key)
                    {
                        existingIndex = search;
                        break;
                    }
                }

                if (existingIndex >= 0)
                    current.Move(existingIndex, index);
                else
                    current.Insert(index, incoming);
            }

            if (!current[index].ContentEquals(incoming))
                current[index].UpdateFrom(incoming);
        }

        while (current.Count > updated.Count)
            current.RemoveAt(current.Count - 1);
    }

    private void ApplyColumnMetadata(BridgeReportColumns columns)
    {
        NameColumnLabel = columns.Name;
        SecondaryColumnLabel = columns.Secondary;
        PrimaryColumnLabel = columns.Primary;
        ShareColumnLabel = columns.Share;
        RateColumnLabel = columns.Rate;
        Detail1ColumnLabel = columns.Detail1;
        Detail2ColumnLabel = columns.Detail2;
        Detail3ColumnLabel = columns.Detail3;
        Detail4ColumnLabel = columns.Detail4;
        SummaryTotalLabel = columns.Total;
        SummaryRateLabel = columns.TotalRate;
        ReportLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyNoEncounter(bool databaseOpen, bool isParserRunning)
    {
        EncounterTitle = GetReportTitle(selectedReport) + " — Running Total";
        EncounterSubtitle = selectedReport == "crafting"
            ? isParserRunning
                ? "Parser active and waiting for synthesis results"
                : "No crafting attempts have been recorded in this parse"
            : isParserRunning
                ? "Parser active and waiting for mob combat data"
                : databaseOpen
                    ? "Parser stopped — press Start to resume parsing"
                    : "Parser stopped — press Start to begin a parse";
        Duration = "00:00";
        currentDurationSeconds = 0;
        currentEncounterName = "All Encounters";
        currentEncounterScope = "all";
        currentFightCount = 0;
        currentEventCount = 0;
        TotalDamage = "0";
        AllianceDps = "0.0";
        EventStatus = selectedReport == "crafting"
            ? isParserRunning
                ? "Active · waiting for crafting events"
                : "No crafting events available"
            : isParserRunning
                ? "Active · waiting for mob combat events"
                : "Stopped · no combat events available";
        Combatants.Clear();
        SelectedCombatant = null;
    }

    private void SetParserState(bool isRunning)
    {
        parserRunning = isRunning;
        ParserStatusBrush = isRunning ? RunningBrush : StoppedBrush;
        ConnectionBadge = isRunning ? "PARSING ACTIVE" : "PARSER STOPPED";
        RaiseControlStateChanged();
    }

    private void RaiseControlStateChanged()
    {
        RaisePropertyChanged(nameof(IsStartEnabled));
        RaisePropertyChanged(nameof(IsStopEnabled));
        RaisePropertyChanged(nameof(IsResetEnabled));
        RaisePropertyChanged(nameof(IsDetectEnabled));
        RaisePropertyChanged(nameof(CanCaptureDotStats));
    }

    private void RequestRefresh()
    {
        RefreshText = "Requesting a fresh KParser snapshot…";
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestEngineCommand(string command)
    {
        EngineCommandRequested?.Invoke(this, new EngineCommandRequestedEventArgs(command));
    }

    private static string GetReportTitle(string report) => report switch
    {
        "damageTaken" => "Damage Taken",
        "healing" => "Healing",
        "buffs" => "Buffs",
        "debuffs" => "Debuffs",
        "deaths" => "Deaths",
        "fights" => "Fights & Performance",
        "experience" => "Experience",
        "chat" => "Chat",
        "loot" => "Item Drops",
        "crafting" => "Crafting",
        _ => "Damage Dealt"
    };

    private static DateTime ParseUtc(string value)
    {
        return DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

internal sealed class EngineCommandRequestedEventArgs(string command) : EventArgs
{
    public string Command { get; } = command;
}
