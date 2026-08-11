using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Infrastructure;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.ViewModels;

internal sealed class CurrentFightViewModel : ObservableObject
{
    private static readonly Brush RunningBrush = CreateBrush(78, 194, 126);
    private static readonly Brush StoppedBrush = CreateBrush(218, 80, 87);

    private string selectedCombatantScope = "all";
    private string selectedFightView = "all";
    private string encounterName = "Waiting for a fight";
    private string encounterState = "Parser stopped";
    private string duration = "00:00";
    private string totalDamage = "0";
    private string allianceDps = "0.0";
    private string eventCount = "0 events";
    private string statusLabel = "PARSER STOPPED";
    private string notice = "Open Sanctum, detect memory, and press Start.";
    private Brush parserStatusBrush = StoppedBrush;
    private bool engineConnected;
    private bool parserRunning;
    private bool engineCommandBusy;
    private bool isAlwaysOnTop;
    private string monitorDisplayMode = "full";
    private double backgroundTransparencyPercent;
    private CombatantRow? selectedCombatant;

    public CurrentFightViewModel(
        string combatantScope,
        string fightView,
        bool alwaysOnTop,
        string displayMode,
        double backgroundTransparency)
    {
        selectedCombatantScope = NormalizeScope(combatantScope);
        selectedFightView = NormalizeFightView(fightView);
        isAlwaysOnTop = alwaysOnTop;
        monitorDisplayMode = NormalizeDisplayMode(displayMode);
        backgroundTransparencyPercent = Math.Clamp(backgroundTransparency, 0, 100);
        Combatants = [];
        StartParserCommand = new DelegateCommand(() => RequestEngineCommand("start"));
        StopParserCommand = new DelegateCommand(() => RequestEngineCommand("stop"));
        ResetParserCommand = new DelegateCommand(() => RequestEngineCommand("reset"));
        ShowFullModeCommand = new DelegateCommand(() => SelectDisplayMode("full"));
        ShowCompactModeCommand = new DelegateCommand(() => SelectDisplayMode("compact"));
        ShowTrueOverlayModeCommand = new DelegateCommand(() => SelectDisplayMode("overlay"));
    }

    public event EventHandler? ScopeChanged;
    public event EventHandler? DisplayModeChanged;
    public event EventHandler<EngineCommandRequestedEventArgs>? EngineCommandRequested;

    public ObservableCollection<CombatantRow> Combatants { get; }
    public ICommand StartParserCommand { get; }
    public ICommand StopParserCommand { get; }
    public ICommand ResetParserCommand { get; }
    public ICommand ShowFullModeCommand { get; }
    public ICommand ShowCompactModeCommand { get; }
    public ICommand ShowTrueOverlayModeCommand { get; }

    public CombatantRow? SelectedCombatant
    {
        get => selectedCombatant;
        set
        {
            if (SetProperty(ref selectedCombatant, value))
            {
                RaisePropertyChanged(nameof(CanSaveBuild));
                RaisePropertyChanged(nameof(CanSendPartySummary));
            }
        }
    }

    public string EncounterName { get => encounterName; private set => SetProperty(ref encounterName, value); }
    public string EncounterState { get => encounterState; private set => SetProperty(ref encounterState, value); }
    public string Duration { get => duration; private set => SetProperty(ref duration, value); }
    public string TotalDamage { get => totalDamage; private set => SetProperty(ref totalDamage, value); }
    public string AllianceDps { get => allianceDps; private set => SetProperty(ref allianceDps, value); }
    public string EventCount { get => eventCount; private set => SetProperty(ref eventCount, value); }
    public string StatusLabel { get => statusLabel; private set => SetProperty(ref statusLabel, value); }
    public string Notice { get => notice; private set => SetProperty(ref notice, value); }
    public string ViewLabel => selectedFightView == "all" ? "ALL MOB FIGHTS" : "CURRENT FIGHT";
    public double BackgroundOpacity => 1.0 - (BackgroundTransparencyPercent / 100.0);

    public Brush ParserStatusBrush
    {
        get => parserStatusBrush;
        private set => SetProperty(ref parserStatusBrush, value);
    }

    public bool IsAlwaysOnTop
    {
        get => isAlwaysOnTop;
        set
        {
            if (SetProperty(ref isAlwaysOnTop, value))
                RaisePropertyChanged(nameof(ShouldStayOnTop));
        }
    }

    public bool IsFullMode => monitorDisplayMode == "full";
    public bool IsCompactMode => monitorDisplayMode == "compact";
    public bool IsTrueOverlayMode => monitorDisplayMode == "overlay";
    public bool ShouldStayOnTop => IsTrueOverlayMode || IsAlwaysOnTop;

    public double BackgroundTransparencyPercent
    {
        get => backgroundTransparencyPercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (SetProperty(ref backgroundTransparencyPercent, normalized))
                RaisePropertyChanged(nameof(BackgroundOpacity));
        }
    }

    public string SelectedCombatantScopeKey => selectedCombatantScope;
    public string SelectedFightViewKey => selectedFightView;
    public string SelectedDisplayModeKey => monitorDisplayMode;

    public bool IsAllFightsView
    {
        get => selectedFightView == "all";
        set { if (value) SelectFightView("all"); }
    }

    public bool IsCurrentFightView
    {
        get => selectedFightView == "current";
        set { if (value) SelectFightView("current"); }
    }

    public bool IsAllianceScope
    {
        get => selectedCombatantScope == "all";
        set { if (value) SelectScope("all"); }
    }

    public bool IsPartyScope
    {
        get => selectedCombatantScope == "party";
        set { if (value) SelectScope("party"); }
    }

    public bool IsSelfScope
    {
        get => selectedCombatantScope == "self";
        set { if (value) SelectScope("self"); }
    }

    public bool IsStartEnabled => engineConnected && !parserRunning && !engineCommandBusy;
    public bool IsStopEnabled => engineConnected && parserRunning && !engineCommandBusy;
    public bool IsResetEnabled => engineConnected && !engineCommandBusy;
    public bool CanSaveBuild => SelectedCombatant is { CombatantType: "Player" };
    public bool CanSendPartySummary => SelectedCombatant is { CombatantType: "Player" };

    public void ApplySnapshot(BridgeSnapshot snapshot)
    {
        engineConnected = true;
        engineCommandBusy = false;
        SetParserState(snapshot.ParserRunning);

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            Notice = snapshot.Error;
            return;
        }

        if (snapshot.Encounter is null)
        {
            EncounterName = selectedFightView == "all" ? "All Mob Fights" : "Waiting for a fight";
            EncounterState = snapshot.ParserRunning
                ? "Parser active · waiting for mob combat"
                : snapshot.DatabaseOpen
                    ? "Parser stopped · press Start to resume"
                    : "Parser stopped · no current parse";
            Duration = "00:00";
            TotalDamage = "0";
            AllianceDps = "0.0";
            EventCount = "0 events";
            Combatants.Clear();
            SelectedCombatant = null;
            Notice = snapshot.ParserRunning
                ? "Live parser ready. The next mob fight will appear automatically."
                : "Start the parser when you are ready to record combat.";
            return;
        }

        var encounter = snapshot.Encounter;
        var generatedUtc = ParseUtc(snapshot.GeneratedUtc);
        var displayDurationSeconds = encounter.DurationSeconds;
        if (encounter.IsActive && snapshot.ParserRunning)
            displayDurationSeconds += Math.Max(0, (DateTime.UtcNow - generatedUtc).TotalSeconds);

        if (selectedFightView == "all")
        {
            var fightWord = encounter.FightCount == 1 ? "fight" : "fights";
            EncounterName = "All Mob Fights";
            EncounterState = snapshot.ParserRunning
                ? $"Running total · {encounter.FightCount:N0} {fightWord} recorded"
                : $"Parser stopped · {encounter.FightCount:N0} {fightWord} retained";
        }
        else
        {
            EncounterName = encounter.IsActive ? encounter.Name : "Last fight: " + encounter.Name;
            EncounterState = encounter.IsActive && snapshot.ParserRunning
                ? "Fight in progress · live totals"
                : snapshot.ParserRunning
                    ? "Last fight ended · waiting for the next mob"
                    : "Parser stopped · last fight retained";
        }
        Duration = FormatDuration(displayDurationSeconds);
        TotalDamage = encounter.TotalDamage.ToString("N0");
        AllianceDps = (encounter.TotalDamage / Math.Max(1.0, displayDurationSeconds)).ToString("N1");
        EventCount = $"{encounter.EventCount:N0} events";
        Notice = "Updated " + generatedUtc.ToLocalTime().ToString("h:mm:ss tt");

        var updatedCombatants = snapshot.Combatants
            .OrderBy(row => row.Rank)
            .Select(combatant => new CombatantRow
            {
                Rank = combatant.Rank,
                Name = combatant.Name,
                Job = string.IsNullOrWhiteSpace(combatant.Job) ? "—" : combatant.Job,
                Damage = combatant.Damage,
                Share = combatant.SharePercent,
                Dps = combatant.Damage / Math.Max(1.0, displayDurationSeconds),
                Melee = combatant.Melee,
                WeaponSkills = combatant.WeaponSkills,
                Magic = combatant.Magic,
                Other = combatant.Other,
                PhysicalAttempts = combatant.PhysicalAttempts,
                PhysicalHits = combatant.PhysicalHits,
                PhysicalMisses = combatant.PhysicalMisses,
                CriticalHits = combatant.CriticalHits,
                TopAction = combatant.TopAction,
                Accuracy = combatant.Accuracy,
                CriticalRate = combatant.CriticalRate,
                CombatantType = combatant.CombatantType,
                IsLocalPlayer = combatant.IsLocalPlayer
            })
            .ToArray();

        SynchronizeCombatants(Combatants, updatedCombatants);
        if (SelectedCombatant is null || !Combatants.Contains(SelectedCombatant))
        {
            SelectedCombatant = Combatants.FirstOrDefault(row => row.IsLocalPlayer)
                                ?? Combatants.FirstOrDefault(row => row.CombatantType == "Player");
        }
    }

    public void SetEngineReady()
    {
        engineConnected = true;
        Notice = "Connected to the bundled parser engine.";
        RaiseControlStateChanged();
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

            if (current[index].Name != incoming.Name)
            {
                var existingIndex = -1;
                for (var search = index + 1; search < current.Count; search++)
                {
                    if (current[search].Name == incoming.Name)
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

    public void SetDisconnected()
    {
        engineConnected = false;
        engineCommandBusy = false;
        parserRunning = false;
        ParserStatusBrush = StoppedBrush;
        StatusLabel = "ENGINE DISCONNECTED";
        EncounterState = "Connection lost · last totals retained";
        Notice = "Waiting for the bundled parser engine.";
        RaiseControlStateChanged();
    }

    public void SetEngineCommandBusy(string command)
    {
        engineCommandBusy = true;
        Notice = command switch
        {
            "start" => "Starting the parser…",
            "stop" => "Stopping the parser safely…",
            "reset" => "Archiving this parse and starting a fresh one…",
            "detect" => "Scanning FFXI chat memory…",
            _ => "Updating parser…"
        };
        RaiseControlStateChanged();
    }

    public void ApplyCommandResult(BridgeCommandResult result)
    {
        engineCommandBusy = false;
        if (result.Success)
            SetParserState(result.ParserRunning);
        else
            RaiseControlStateChanged();

        Notice = result.Success
            ? result.Message
            : "Could not " + result.Command + ": " + result.Message;
    }

    public void SetUserNotice(string message)
    {
        Notice = message;
    }

    private void SelectScope(string scope)
    {
        scope = NormalizeScope(scope);
        if (selectedCombatantScope == scope)
            return;

        selectedCombatantScope = scope;
        RaisePropertyChanged(nameof(IsAllianceScope));
        RaisePropertyChanged(nameof(IsPartyScope));
        RaisePropertyChanged(nameof(IsSelfScope));
        ScopeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectFightView(string fightView)
    {
        fightView = NormalizeFightView(fightView);
        if (selectedFightView == fightView)
            return;

        selectedFightView = fightView;
        RaisePropertyChanged(nameof(IsAllFightsView));
        RaisePropertyChanged(nameof(IsCurrentFightView));
        RaisePropertyChanged(nameof(ViewLabel));
        ScopeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectDisplayMode(string displayMode)
    {
        displayMode = NormalizeDisplayMode(displayMode);
        if (monitorDisplayMode == displayMode)
            return;

        monitorDisplayMode = displayMode;
        RaisePropertyChanged(nameof(IsFullMode));
        RaisePropertyChanged(nameof(IsCompactMode));
        RaisePropertyChanged(nameof(IsTrueOverlayMode));
        RaisePropertyChanged(nameof(ShouldStayOnTop));
        RaisePropertyChanged(nameof(SelectedDisplayModeKey));
        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetParserState(bool isRunning)
    {
        parserRunning = isRunning;
        ParserStatusBrush = isRunning ? RunningBrush : StoppedBrush;
        StatusLabel = isRunning ? "PARSING ACTIVE" : "PARSER STOPPED";
        RaiseControlStateChanged();
    }

    private void RaiseControlStateChanged()
    {
        RaisePropertyChanged(nameof(IsStartEnabled));
        RaisePropertyChanged(nameof(IsStopEnabled));
        RaisePropertyChanged(nameof(IsResetEnabled));
    }

    private void RequestEngineCommand(string command)
    {
        EngineCommandRequested?.Invoke(this, new EngineCommandRequestedEventArgs(command));
    }

    private static string NormalizeScope(string scope) => scope switch
    {
        "party" => "party",
        "players" => "self",
        "self" => "self",
        _ => "all"
    };

    private static string NormalizeFightView(string fightView) =>
        fightView == "current" ? "current" : "all";

    private static string NormalizeDisplayMode(string displayMode) => displayMode switch
    {
        "compact" => "compact",
        "overlay" => "overlay",
        _ => "full"
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
        var durationValue = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return durationValue.TotalHours >= 1
            ? $"{(int)durationValue.TotalHours:00}:{durationValue.Minutes:00}:{durationValue.Seconds:00}"
            : $"{durationValue.Minutes:00}:{durationValue.Seconds:00}";
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
