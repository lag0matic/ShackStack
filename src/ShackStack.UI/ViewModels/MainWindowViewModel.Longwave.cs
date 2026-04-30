using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private ObservableCollection<LongwaveSpotSummaryItem> longwavePotaSpots = [];

    [ObservableProperty]
    private LongwaveSpotSummaryItem? selectedLongwavePotaSpot;

    [ObservableProperty]
    private ObservableCollection<LongwaveSpotSummaryItem> voiceLongwavePotaSpots = [];

    [ObservableProperty]
    private LongwaveSpotSummaryItem? selectedVoiceLongwavePotaSpot;

    [ObservableProperty]
    private IReadOnlyList<string> voiceLongwaveBandFilterOptions =
    [
        "All bands",
        "160m",
        "80m",
        "40m",
        "30m",
        "20m",
        "17m",
        "15m",
        "12m",
        "10m",
        "6m",
    ];

    [ObservableProperty]
    private string selectedVoiceLongwaveBandFilter = "All bands";

    [ObservableProperty]
    private string voicePotaSpotComment = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotCallsign = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotParkReference = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotFrequencyKhz = "14286.0";

    [ObservableProperty]
    private string voicePotaSpotMode = "SSB";

    [ObservableProperty]
    private ObservableCollection<LongwaveLogbookItem> longwaveLogbooks = [];

    [ObservableProperty]
    private LongwaveLogbookItem? selectedLongwaveLogbook;

    [ObservableProperty]
    private ObservableCollection<LongwaveRecentContactItem> longwaveRecentContacts = [];

    [ObservableProperty]
    private LongwaveRecentContactItem? selectedLongwaveRecentContact;

    [ObservableProperty]
    private string longwaveNewLogbookName = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookName = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookOperatorCallsign = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookParkReference = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookActivationDate = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookNotes = string.Empty;

    [ObservableProperty]
    private bool isLongwaveBusy;

    private bool _longwaveEnsureLoadInFlight;

    [ObservableProperty]
    private string longwaveStatus = "Longwave integration disabled.";

    [ObservableProperty]
    private string longwaveOperatorSummary = "Longwave integration disabled.";

    [ObservableProperty]
    private string longwaveLogStatus = "Ready to log from rig or selected spot.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingLongwaveContact))]
    [NotifyPropertyChangedFor(nameof(IsCreatingLongwaveContact))]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveEditingContactId = string.Empty;

    public bool IsEditingLongwaveContact => !string.IsNullOrWhiteSpace(LongwaveEditingContactId);
    public bool IsCreatingLongwaveContact => !IsEditingLongwaveContact;

    public string LongwaveContactEditorSummary => IsEditingLongwaveContact
        ? $"Editing {LongwaveLogCallsign} from {LongwaveLogQsoDate} {LongwaveLogTimeOn} UTC."
        : "New contact. Use the rig, a spot, or the fields below.";

    public string LongwaveQuickLogSummary
    {
        get
        {
            var station = string.IsNullOrWhiteSpace(LongwaveLogCallsign)
                ? "no station selected"
                : FormatCallsign(LongwaveLogCallsign);
            var logbook = SelectedLongwaveLogbook?.Name ?? "default logbook";
            var mode = string.IsNullOrWhiteSpace(LongwaveLogMode) ? "mode ?" : LongwaveLogMode.Trim().ToUpperInvariant();
            var band = string.IsNullOrWhiteSpace(LongwaveLogBand) ? "band ?" : LongwaveLogBand.Trim();
            var frequency = double.TryParse(LongwaveLogFrequencyKhz, out var khz) && khz > 0
                ? $"{khz / 1000d:0.000000} MHz"
                : "freq ?";
            var report = string.IsNullOrWhiteSpace(LongwaveLogRstSent) && string.IsNullOrWhiteSpace(LongwaveLogRstReceived)
                ? "report --/--"
                : $"report {LongwaveLogRstSent}/{LongwaveLogRstReceived}";

            return $"Will log {station} | {band} {mode} | {frequency} | {report} | {logbook}";
        }
    }

    public string LongwaveSelectedLogbookQrzUploadSummary
    {
        get
        {
            if (SelectedLongwaveLogbook is null)
            {
                return "Select a logbook to see QRZ upload state.";
            }

            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            var pending = Math.Max(0, LongwaveRecentContacts.Count - uploaded);
            return $"{SelectedLongwaveLogbook.Name}: {pending} QRZ pending, {uploaded} uploaded.";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogOperatorCallsign = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogCallsign = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveLogQsoDate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveLogTimeOn = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogMode = "SSB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogBand = "20m";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogFrequencyKhz = "14074.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogRstSent = "59";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogRstReceived = "59";

    [ObservableProperty]
    private string longwaveLogParkReference = string.Empty;

    [ObservableProperty]
    private string longwaveLogGridSquare = string.Empty;

    [ObservableProperty]
    private string longwaveLogName = string.Empty;

    [ObservableProperty]
    private string longwaveLogQth = string.Empty;

    [ObservableProperty]
    private string longwaveLogCounty = string.Empty;

    [ObservableProperty]
    private string longwaveLogState = string.Empty;

    [ObservableProperty]
    private string longwaveLogCountry = string.Empty;

    [ObservableProperty]
    private string longwaveLogDxcc = string.Empty;

    private double? _longwaveLogLatitude;
    private double? _longwaveLogLongitude;

    [RelayCommand]
    private async Task RefreshLongwaveSpotsAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveStatus = "Refreshing POTA spots...";
            var settings = BuildCurrentLongwaveSettings();
            var context = await _longwaveService.GetOperatorContextAsync(settings, CancellationToken.None);
            var logbooks = await _longwaveService.GetLogbooksAsync(settings, CancellationToken.None);
            var spots = await _longwaveService.GetPotaSpotsAsync(settings, CancellationToken.None);
            LongwaveOperatorSummary = $"Longwave operator {context.Callsign}  |  {spots.Count} POTA spots loaded";
            LongwaveLogbooks = new ObservableCollection<LongwaveLogbookItem>(
                logbooks.Select(ToLongwaveLogbookItem));
            SelectedLongwaveLogbook = SelectPreferredLongwaveLogbook(LongwaveLogbooks, SelectedLongwaveLogbook, settings.DefaultLogbookName);
            var contacts = await _longwaveService.GetContactsAsync(settings, SelectedLongwaveLogbook?.Id, CancellationToken.None);
            LongwavePotaSpots = new ObservableCollection<LongwaveSpotSummaryItem>(
                spots.Select(spot => ToLongwaveSpotSummaryItem(spot, _longwaveLoggedContactKeys.Contains(spot.Id))));
            RebuildVoiceLongwavePotaSpots();
            LongwaveRecentContacts = new ObservableCollection<LongwaveRecentContactItem>(
                contacts.Take(50).Select(ToLongwaveRecentContactItem));
            LongwaveStatus = spots.Count == 0
                ? "No POTA spots returned from Longwave."
                : $"Loaded {spots.Count} POTA spots from Longwave.";
            if (SelectedLongwaveLogbook is not null)
            {
                LongwaveLogStatus = $"Using Longwave logbook {SelectedLongwaveLogbook.Name}.";
            }
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    public async Task EnsureLongwaveDataLoadedAsync()
    {
        if (_longwaveService is null
            || _longwaveEnsureLoadInFlight
            || IsLongwaveBusy
            || LongwaveLogbooks.Count > 0)
        {
            return;
        }

        var settings = BuildCurrentLongwaveSettings();
        if (!settings.Enabled
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ClientApiToken))
        {
            return;
        }

        try
        {
            _longwaveEnsureLoadInFlight = true;
            await RefreshLongwaveSpotsAsync();
        }
        finally
        {
            _longwaveEnsureLoadInFlight = false;
        }
    }

    [RelayCommand]
    private async Task TuneSelectedLongwaveSpotAsync()
    {
        if (SelectedLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a POTA spot first.";
            return;
        }

        await TuneRadioForLongwaveSpotAsync(SelectedLongwavePotaSpot);
    }

    [RelayCommand]
    private async Task TuneSelectedVoiceLongwaveSpotAsync()
    {
        if (SelectedVoiceLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a voice POTA spot first.";
            return;
        }

        SelectedLongwavePotaSpot = SelectedVoiceLongwavePotaSpot;
        await TuneRadioForLongwaveSpotAsync(SelectedVoiceLongwavePotaSpot);
    }

    [RelayCommand]
    private async Task WorkSelectedVoiceLongwaveSpotAsync()
    {
        if (SelectedVoiceLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a voice POTA spot first.";
            return;
        }

        SelectedLongwavePotaSpot = SelectedVoiceLongwavePotaSpot;
        ApplySpotToLongwaveLog(SelectedVoiceLongwavePotaSpot);
        await TuneRadioForLongwaveSpotAsync(SelectedVoiceLongwavePotaSpot);
        LongwaveLogStatus = $"Ready to work {SelectedVoiceLongwavePotaSpot.ActivatorCallsign} at {SelectedVoiceLongwavePotaSpot.ParkReference}.";
    }

    [RelayCommand]
    private void UseRigForVoicePotaSpot()
    {
        VoicePotaSpotFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        VoicePotaSpotMode = MapRadioModeToPotaMode(SelectedMode);
        if (string.IsNullOrWhiteSpace(VoicePotaSpotCallsign))
        {
            VoicePotaSpotCallsign = FormatCallsign(LongwaveLogCallsign);
        }

        if (string.IsNullOrWhiteSpace(VoicePotaSpotParkReference))
        {
            VoicePotaSpotParkReference = LongwaveLogParkReference.Trim().ToUpperInvariant();
        }

        LongwaveStatus = $"Spot form filled from rig: {VoicePotaSpotFrequencyKhz} kHz {VoicePotaSpotMode}.";
    }

    [RelayCommand]
    private void UseSelectedSpotForLongwaveLog()
    {
        if (SelectedLongwavePotaSpot is null)
        {
            LongwaveLogStatus = "Select a POTA spot first.";
            return;
        }

        ApplySpotToLongwaveLog(SelectedLongwavePotaSpot);
        LongwaveLogStatus = $"Prefilled log from {SelectedLongwavePotaSpot.ActivatorCallsign} at {SelectedLongwavePotaSpot.ParkReference}.";
    }

    [RelayCommand]
    private void UseRigForLongwaveLog()
    {
        LongwaveLogFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        LongwaveLogBand = DeriveBandFromFrequencyKhz(CurrentFrequencyHz / 1000d);
        LongwaveLogMode = MapRadioModeToLogMode(SelectedMode);
        LongwaveLogOperatorCallsign = FormatCallsign(SettingsCallsign);
        LongwaveLogGridSquare = SettingsGridSquare.Trim().ToUpperInvariant();
        LongwaveLogStatus = $"Prefilled log from rig: {LongwaveLogBand} {LongwaveLogMode} at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private async Task PostVoicePotaSpotAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        var activatorCall = FormatCallsign(VoicePotaSpotCallsign);
        var park = VoicePotaSpotParkReference.Trim().ToUpperInvariant();
        var mode = string.IsNullOrWhiteSpace(VoicePotaSpotMode)
            ? MapRadioModeToPotaMode(SelectedMode)
            : VoicePotaSpotMode.Trim().ToUpperInvariant();
        var spotterCall = FormatCallsign(
            string.IsNullOrWhiteSpace(LongwaveLogOperatorCallsign)
                ? SettingsCallsign
                : LongwaveLogOperatorCallsign);

        if (string.IsNullOrWhiteSpace(activatorCall) || string.IsNullOrWhiteSpace(park))
        {
            LongwaveStatus = "POTA spot needs an activator callsign and park reference.";
            return;
        }

        if (!double.TryParse(VoicePotaSpotFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveStatus = "POTA spot needs a valid frequency in kHz.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveStatus = $"Posting POTA spot for {activatorCall}...";
            var band = DeriveBandFromFrequencyKhz(frequencyKhz);
            var created = await _longwaveService.CreatePotaSpotAsync(
                BuildCurrentLongwaveSettings(),
                new LongwavePotaSpotDraft(
                    activatorCall,
                    park,
                    frequencyKhz,
                    mode,
                    band,
                    VoicePotaSpotComment,
                    spotterCall),
                CancellationToken.None);

            var item = ToLongwaveSpotSummaryItem(created, isLogged: false);
            LongwavePotaSpots.Insert(0, item);
            SelectedLongwavePotaSpot = item;
            RebuildVoiceLongwavePotaSpots();
            SelectedVoiceLongwavePotaSpot = VoiceLongwavePotaSpots.FirstOrDefault(spot => spot.Id == item.Id) ?? SelectedVoiceLongwavePotaSpot;
            VoicePotaSpotComment = string.Empty;
            VoicePotaSpotCallsign = string.Empty;
            VoicePotaSpotParkReference = string.Empty;
            LongwaveStatus = $"Posted POTA spot for {created.ActivatorCallsign} at {created.ParkReference}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private void UseRigForCwLongwaveLog()
    {
        UseRigForModeLongwaveLog("CW", "599", "599");
        LongwaveLogStatus = $"Prefilled CW log from rig: {LongwaveLogBand} CW at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForRttyLongwaveLog()
    {
        UseRigForModeLongwaveLog("RTTY", "599", "599");
        LongwaveLogStatus = $"Prefilled RTTY log from rig: {LongwaveLogBand} RTTY at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForKeyboardLongwaveLog()
    {
        var mode = string.IsNullOrWhiteSpace(KeyboardSelectedMode)
            ? "PSK"
            : KeyboardSelectedMode.Trim().ToUpperInvariant();
        UseRigForModeLongwaveLog(mode, "599", "599");
        LongwaveLogStatus = $"Prefilled {LongwaveLogMode} log from rig: {LongwaveLogBand} at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForSstvLongwaveLog()
    {
        UseRigForModeLongwaveLog("SSTV", "595", "595");
        if (!string.IsNullOrWhiteSpace(SstvDecodedFskIdCallsign))
        {
            LongwaveLogCallsign = FormatCallsign(SstvDecodedFskIdCallsign);
        }

        LongwaveLogStatus = $"Prefilled SSTV log from rig: {LongwaveLogBand} SSTV at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForFreedvLongwaveLog()
    {
        UseRigForModeLongwaveLog("FREEDV", "59", "59");
        var callsign = NormalizeFreedvRadeCallsign(FreedvLastRadeCallsign);
        if (callsign is not null)
        {
            LongwaveLogCallsign = callsign;
        }

        LongwaveLogStatus = $"Prefilled FreeDV log from rig: {LongwaveLogBand} FreeDV at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseDecodedSstvFskIdForLog()
    {
        var callsign = FormatCallsign(SstvDecodedFskIdCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            LongwaveLogStatus = "No SSTV FSKID callsign has been decoded yet.";
            return;
        }

        LongwaveLogCallsign = callsign;
        if (string.IsNullOrWhiteSpace(LongwaveLogMode) || string.Equals(LongwaveLogMode, "SSB", StringComparison.OrdinalIgnoreCase))
        {
            UseRigForModeLongwaveLog("SSTV", "595", "595");
            LongwaveLogCallsign = callsign;
        }

        LongwaveLogStatus = $"Using SSTV FSKID {callsign} for the log.";
    }

    [RelayCommand]
    private void UseFreedvRadeCallForLog()
    {
        var callsign = NormalizeFreedvRadeCallsign(FreedvLastRadeCallsign);
        if (callsign is null)
        {
            LongwaveLogStatus = "No valid FreeDV RADE callsign has been decoded yet.";
            return;
        }

        LongwaveLogCallsign = callsign;
        if (string.IsNullOrWhiteSpace(LongwaveLogMode) || string.Equals(LongwaveLogMode, "SSB", StringComparison.OrdinalIgnoreCase))
        {
            UseRigForModeLongwaveLog("FREEDV", "59", "59");
            LongwaveLogCallsign = callsign;
        }

        LongwaveLogStatus = $"Using FreeDV RADE callsign {callsign} for the log.";
    }

    private void UseRigForModeLongwaveLog(string mode, string sentReport, string receivedReport)
    {
        UseRigForLongwaveLog();
        LongwaveLogMode = mode.Trim().ToUpperInvariant();
        LongwaveLogRstSent = sentReport;
        LongwaveLogRstReceived = receivedReport;
        OnPropertyChanged(nameof(LongwaveQuickLogSummary));
    }


    [RelayCommand]
    private async Task AutofillLongwaveLookupAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        var callsign = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            LongwaveLogStatus = "Enter or select a callsign first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = $"Looking up {callsign} via Longwave/QRZ...";
            var lookup = await _longwaveService.LookupCallsignAsync(BuildCurrentLongwaveSettings(), callsign, CancellationToken.None);
            ApplyLongwaveLookup(lookup);
            LongwaveLogStatus = $"Autofilled location for {lookup.Callsign}.";
        }
        catch (Exception ex)
        {
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogCurrentQsoAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        if (!double.TryParse(LongwaveLogFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveLogStatus = "Enter a valid frequency in kHz.";
            return;
        }

        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        var stationCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(operatorCall) || string.IsNullOrWhiteSpace(stationCall))
        {
            LongwaveLogStatus = "Operator and station callsigns are required.";
            return;
        }

        var qsoTime = GetLongwaveEditorQsoTimeUtc();
        var logKey = BuildLongwaveLogDedupeKey(stationCall, LongwaveLogMode, frequencyKhz, qsoTime);
        if (!_longwaveLoggedContactKeys.Add(logKey))
        {
            LongwaveLogStatus = "This QSO was already logged from this session.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = "Posting contact to Longwave...";
            var settings = BuildCurrentLongwaveSettings();
            var logbook = SelectedLongwaveLogbook is not null
                ? new LongwaveLogbook(
                    SelectedLongwaveLogbook.Id,
                    SelectedLongwaveLogbook.Name,
                    SelectedLongwaveLogbook.OperatorCallsign,
                    SelectedLongwaveLogbook.ParkReference,
                    SelectedLongwaveLogbook.ActivationDate,
                    SelectedLongwaveLogbook.Notes,
                    SelectedLongwaveLogbook.ContactCount)
                : await _longwaveService.GetOrCreateLogbookAsync(settings, operatorCall, CancellationToken.None);
            EnsureLongwaveLogbookSelected(logbook);
            var draft = BuildLongwaveContactDraft(logbook.Id, stationCall, operatorCall, frequencyKhz, qsoTime);
            var created = await _longwaveService.CreateContactAsync(
                settings,
                draft,
                CancellationToken.None);

            UpsertLongwaveRecentContact(created);
            LongwaveLogStatus = BuildLongwaveLoggedContactSummary(created, logbook.Name);
            LongwaveStatus = $"Longwave logged {created.StationCallsign} on {created.Band} {created.Mode}.";
            MarkLongwaveSpotLogged(created.SourceSpotId);
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            _longwaveLoggedContactKeys.Remove(logKey);
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedLongwaveContactAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveRecentContact is null || string.IsNullOrWhiteSpace(LongwaveEditingContactId))
        {
            LongwaveLogStatus = "Select a logged contact before saving changes.";
            return;
        }

        if (!double.TryParse(LongwaveLogFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveLogStatus = "Enter a valid frequency in kHz.";
            return;
        }

        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        var stationCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(operatorCall) || string.IsNullOrWhiteSpace(stationCall))
        {
            LongwaveLogStatus = "Operator and station callsigns are required.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = $"Saving changes for {stationCall}...";
            var logbookId = SelectedLongwaveLogbook?.Id ?? SelectedLongwaveRecentContact.LogbookId;
            var draft = BuildLongwaveContactDraft(logbookId, stationCall, operatorCall, frequencyKhz, GetLongwaveEditorQsoTimeUtc());
            var updated = await _longwaveService.UpdateContactAsync(
                BuildCurrentLongwaveSettings(),
                LongwaveEditingContactId,
                draft,
                CancellationToken.None);
            UpsertLongwaveRecentContact(updated);
            LongwaveEditingContactId = updated.Id;
            LongwaveLogStatus = BuildLongwaveLoggedContactSummary(updated, SelectedLongwaveLogbook?.Name ?? "Longwave");
            LongwaveStatus = $"Longwave updated {updated.StationCallsign} on {updated.Band} {updated.Mode}.";
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private void NewLongwaveContact()
    {
        SelectedLongwaveRecentContact = null;
        LongwaveEditingContactId = string.Empty;
        LongwaveLogCallsign = string.Empty;
        LongwaveLogQsoDate = string.Empty;
        LongwaveLogTimeOn = string.Empty;
        LongwaveLogParkReference = string.Empty;
        LongwaveLogName = string.Empty;
        LongwaveLogQth = string.Empty;
        LongwaveLogCounty = string.Empty;
        LongwaveLogState = string.Empty;
        LongwaveLogCountry = string.Empty;
        LongwaveLogDxcc = string.Empty;
        _longwaveLogLatitude = null;
        _longwaveLogLongitude = null;
        LongwaveLogStatus = "Ready for a new Longwave contact.";
    }

    [RelayCommand]
    private async Task CreateLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        var name = LongwaveNewLogbookName.Trim();
        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        if (string.IsNullOrWhiteSpace(name))
        {
            LongwaveStatus = "Enter a logbook name first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(operatorCall))
        {
            LongwaveStatus = "Operator callsign is required to create a logbook.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var settings = BuildCurrentLongwaveSettings();
            var created = await _longwaveService.CreateLogbookAsync(settings, name, operatorCall, null, CancellationToken.None);
            LongwaveLogbooks.Insert(0, ToLongwaveLogbookItem(created));
            SelectedLongwaveLogbook = LongwaveLogbooks.FirstOrDefault(item => item.Id == created.Id);
            LongwaveNewLogbookName = string.Empty;
            await RefreshLongwaveContactsAsync();
            LongwaveStatus = $"Created logbook {created.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook first.";
            return;
        }

        var name = LongwaveSelectedLogbookName.Trim();
        var operatorCall = FormatCallsign(LongwaveSelectedLogbookOperatorCallsign);
        if (string.IsNullOrWhiteSpace(name))
        {
            LongwaveStatus = "Logbook name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(operatorCall))
        {
            LongwaveStatus = "Logbook operator callsign is required.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            var updated = await _longwaveService.UpdateLogbookAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                name,
                operatorCall,
                LongwaveSelectedLogbookParkReference,
                LongwaveSelectedLogbookActivationDate,
                LongwaveSelectedLogbookNotes,
                CancellationToken.None);
            var updatedItem = ToLongwaveLogbookItem(updated);
            var index = LongwaveLogbooks.IndexOf(target);
            if (index >= 0)
            {
                LongwaveLogbooks[index] = updatedItem;
            }
            else
            {
                LongwaveLogbooks.Insert(0, updatedItem);
            }

            SelectedLongwaveLogbook = updatedItem;
            LongwaveStatus = $"Updated logbook {updated.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            await _longwaveService.DeleteLogbookAsync(BuildCurrentLongwaveSettings(), target.Id, CancellationToken.None);
            LongwaveLogbooks.Remove(target);
            SelectedLongwaveLogbook = LongwaveLogbooks.FirstOrDefault();
            await RefreshLongwaveContactsAsync();
            LongwaveStatus = $"Deleted logbook {target.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportSelectedLongwaveLogbookAdifAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook before exporting ADIF.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            LongwaveStatus = $"Exporting {target.Name} ADIF...";
            var adif = await _longwaveService.ExportLogbookAdifAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                CancellationToken.None);

            var exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "longwave-adif");
            Directory.CreateDirectory(exportRoot);
            var safeName = MakeSafeFileName(target.Name);
            var path = Path.Combine(exportRoot, $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.adi");
            await File.WriteAllTextAsync(path, adif, CancellationToken.None);
            LongwaveStatus = $"Exported ADIF to {path}.";
            LongwaveLogStatus = LongwaveStatus;
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedLongwaveContactAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveRecentContact is null)
        {
            LongwaveStatus = "Select a contact first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveRecentContact;
            await _longwaveService.DeleteContactAsync(BuildCurrentLongwaveSettings(), target.Id, CancellationToken.None);
            LongwaveRecentContacts.Remove(target);
            SelectedLongwaveRecentContact = LongwaveRecentContacts.FirstOrDefault();
            LongwaveStatus = $"Deleted contact {target.StationCallsign}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLongwaveContactsNowAsync()
    {
        try
        {
            IsLongwaveBusy = true;
            await RefreshLongwaveContactsAsync();
            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            var pending = Math.Max(0, LongwaveRecentContacts.Count - uploaded);
            LongwaveStatus = $"Loaded {LongwaveRecentContacts.Count} contacts. QRZ uploaded {uploaded}, pending {pending}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UploadSelectedLongwaveLogbookToQrzAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook before uploading to QRZ.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            var pending = LongwaveRecentContacts.Count(static contact =>
                !string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            if (pending <= 0)
            {
                LongwaveStatus = $"{target.Name} has no QRZ-pending contacts.";
                LongwaveLogStatus = LongwaveStatus;
                return;
            }

            LongwaveStatus = $"Uploading {pending} pending contact(s) from {target.Name} to QRZ via Longwave...";
            var result = await _longwaveService.UploadLogbookToQrzAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                CancellationToken.None);
            await RefreshLongwaveContactsAsync();
            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            LongwaveStatus = result.Uploaded
                ? $"{result.Message} Refreshed {uploaded} uploaded contact badge(s)."
                : result.Message;
            LongwaveLogStatus = LongwaveStatus;
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    private async Task TuneRadioForLongwaveSpotAsync(LongwaveSpotSummaryItem spot)
    {
        if (_radioService is null || CanConnect)
        {
            LongwaveStatus = "Connect the radio before tuning a spot.";
            return;
        }

        var hz = (long)Math.Round(spot.FrequencyKhz * 1000d);
        var radioMode = MapSpotModeToRadioMode(spot.Mode, hz);

        try
        {
            await _radioService.SetModeAsync(radioMode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            LongwaveStatus = $"Tuned {spot.ActivatorCallsign} on {spot.FrequencyText} {spot.Mode}.";
            RadioStatusSummary = $"POTA tuned: {hz:N0} Hz {FormatModeDisplay(radioMode)}";

            if (IsWeakSignalSpotMode(spot.Mode))
            {
                SelectedModePanelTabIndex = 3;
                WsjtxSelectedMode = NormalizeWeakSignalMode(spot.Mode);
            }
        }
        catch (Exception ex)
        {
            LongwaveStatus = $"Spot tune failed: {ex.Message}";
        }
    }

    private void ApplySpotToLongwaveLog(LongwaveSpotSummaryItem spot)
    {
        LongwaveLogCallsign = spot.ActivatorCallsign;
        LongwaveLogMode = spot.Mode.ToUpperInvariant();
        LongwaveLogBand = spot.Band;
        LongwaveLogFrequencyKhz = $"{spot.FrequencyKhz:0.0}";
        LongwaveLogParkReference = spot.ParkReference;
        LongwaveLogGridSquare = string.Empty;
        if (IsWeakSignalSpotMode(spot.Mode))
        {
            LongwaveLogRstSent = "-10";
            LongwaveLogRstReceived = "-10";
        }
    }

    private void ApplyLongwaveLookup(LongwaveCallsignLookup lookup)
    {
        LongwaveLogCallsign = lookup.Callsign;
        LongwaveLogName = lookup.Name ?? string.Empty;
        LongwaveLogQth = lookup.Qth ?? string.Empty;
        LongwaveLogCounty = lookup.County ?? string.Empty;
        LongwaveLogGridSquare = lookup.GridSquare?.Trim().ToUpperInvariant() ?? LongwaveLogGridSquare;
        LongwaveLogCountry = lookup.Country ?? string.Empty;
        LongwaveLogState = lookup.State?.Trim().ToUpperInvariant() ?? string.Empty;
        LongwaveLogDxcc = lookup.Dxcc ?? string.Empty;
        _longwaveLogLatitude = lookup.Latitude;
        _longwaveLogLongitude = lookup.Longitude;
    }


    private void ApplyLongwaveSettingsState(AppSettings settings)
    {
        var current = BuildCurrentLongwaveSettings(settings);
        if (current.Enabled)
        {
            if (string.IsNullOrWhiteSpace(LongwaveStatus)
                || string.Equals(LongwaveStatus, "Longwave integration disabled.", StringComparison.Ordinal)
                || string.Equals(LongwaveStatus, "Enable Longwave in Settings to use POTA spots and logging.", StringComparison.Ordinal))
            {
                LongwaveStatus = "Longwave ready. Refresh spots or log a contact.";
            }

            LongwaveOperatorSummary = "Longwave integration enabled.";

            if (string.IsNullOrWhiteSpace(LongwaveLogStatus)
                || string.Equals(LongwaveLogStatus, "Enable Longwave in Settings to log contacts here.", StringComparison.Ordinal))
            {
                LongwaveLogStatus = "Ready to log from rig or selected spot.";
            }
        }
        else
        {
            LongwaveStatus = "Enable Longwave in Settings to use POTA spots and logging.";
            LongwaveOperatorSummary = "Longwave integration disabled.";
            LongwaveLogStatus = "Enable Longwave in Settings to log contacts here.";
        }
    }

    private LongwaveSettings BuildCurrentLongwaveSettings() => BuildCurrentLongwaveSettings(_settings);

    private LongwaveSettings BuildCurrentLongwaveSettings(AppSettings fallback) =>
        new(
            SettingsLongwaveEnabled,
            string.IsNullOrWhiteSpace(SettingsLongwaveBaseUrl) ? fallback.Longwave.BaseUrl : SettingsLongwaveBaseUrl.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveClientApiToken) ? fallback.Longwave.ClientApiToken : SettingsLongwaveClientApiToken.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveDefaultLogbookName) ? fallback.Longwave.DefaultLogbookName : SettingsLongwaveDefaultLogbookName.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveDefaultLogbookNotes) ? fallback.Longwave.DefaultLogbookNotes : SettingsLongwaveDefaultLogbookNotes.Trim());

    private static LongwaveSpotSummaryItem ToLongwaveSpotSummaryItem(LongwaveSpot spot, bool isLogged) =>
        new(
            spot.Id,
            spot.ActivatorCallsign,
            spot.ParkReference,
            spot.FrequencyKhz,
            spot.Mode,
            spot.Band,
            spot.Comments,
            spot.SpotterCallsign,
            spot.SpottedAtUtc,
            isLogged);

    private static LongwaveRecentContactItem ToLongwaveRecentContactItem(LongwaveContact contact) =>
        new(
            contact.Id,
            contact.LogbookId,
            contact.StationCallsign,
            contact.OperatorCallsign,
            contact.QsoDate,
            contact.TimeOn,
            contact.Mode,
            contact.Band,
            $"{contact.QsoDate} {contact.TimeOn}",
            contact.ParkReference,
            contact.FrequencyKhz,
            contact.RstSent,
            contact.RstReceived,
            contact.Name,
            contact.Qth,
            contact.County,
            contact.GridSquare,
            contact.Country,
            contact.State,
            contact.Dxcc,
            contact.QrzUploadStatus,
            contact.QrzUploadDate,
            contact.Latitude,
            contact.Longitude,
            contact.SourceSpotId);

    private static LongwaveLogbookItem ToLongwaveLogbookItem(LongwaveLogbook logbook) =>
        new(
            logbook.Id,
            logbook.Name,
            logbook.OperatorCallsign,
            logbook.ParkReference,
            logbook.ActivationDate,
            logbook.Notes,
            logbook.ContactCount);

    private LongwaveContactDraft BuildLongwaveContactDraft(
        string logbookId,
        string stationCall,
        string operatorCall,
        double frequencyKhz,
        DateTime qsoTimeUtc) =>
        new(
            logbookId,
            stationCall,
            operatorCall,
            qsoTimeUtc.ToString("yyyyMMdd"),
            qsoTimeUtc.ToString("HHmmss"),
            string.IsNullOrWhiteSpace(LongwaveLogBand) ? DeriveBandFromFrequencyKhz(frequencyKhz) : LongwaveLogBand.Trim(),
            LongwaveLogMode.Trim().ToUpperInvariant(),
            frequencyKhz,
            LongwaveLogParkReference,
            LongwaveLogRstSent,
            LongwaveLogRstReceived,
            LongwaveLogName,
            LongwaveLogQth,
            LongwaveLogCounty,
            LongwaveLogGridSquare,
            LongwaveLogCountry,
            LongwaveLogState,
            LongwaveLogDxcc,
            _longwaveLogLatitude,
            _longwaveLogLongitude,
            SelectedLongwavePotaSpot is not null
                && string.Equals(SelectedLongwavePotaSpot.ActivatorCallsign, stationCall, StringComparison.OrdinalIgnoreCase)
                    ? SelectedLongwavePotaSpot.Id
                    : SelectedLongwaveRecentContact?.SourceSpotId);

    private static string BuildLongwaveLoggedContactSummary(LongwaveContact contact, string logbookName)
    {
        var report = string.IsNullOrWhiteSpace(contact.RstSent) && string.IsNullOrWhiteSpace(contact.RstReceived)
            ? "no report"
            : $"{contact.RstSent ?? "--"}/{contact.RstReceived ?? "--"}";
        var park = string.IsNullOrWhiteSpace(contact.ParkReference) ? string.Empty : $" | {contact.ParkReference}";
        return $"Logged {contact.StationCallsign} | {contact.QsoDate} {contact.TimeOn} UTC | {contact.Band} {contact.Mode} | {contact.FrequencyKhz / 1000d:0.000000} MHz | RST {report} | {logbookName}{park}.";
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "longwave-logbook" : safe;
    }

    private DateTime GetLongwaveEditorQsoTimeUtc()
    {
        var date = LongwaveLogQsoDate.Trim();
        var time = LongwaveLogTimeOn.Trim();
        if (date.Length == 8
            && DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            if (time.Length is > 0 and < 6)
            {
                time = time.PadRight(6, '0');
            }

            if (time.Length == 6
                && TimeSpan.TryParseExact(time, "hhmmss", null, out var parsedTime))
            {
                return DateTime.SpecifyKind(parsedDate.Date + parsedTime, DateTimeKind.Utc);
            }
        }

        var now = DateTime.UtcNow;
        LongwaveLogQsoDate = now.ToString("yyyyMMdd");
        LongwaveLogTimeOn = now.ToString("HHmmss");
        return now;
    }

    private void ApplyLongwaveContactToEditor(LongwaveRecentContactItem contact)
    {
        LongwaveEditingContactId = contact.Id;
        var matchingLogbook = LongwaveLogbooks.FirstOrDefault(logbook => string.Equals(logbook.Id, contact.LogbookId, StringComparison.Ordinal));
        if (matchingLogbook is not null
            && !string.Equals(SelectedLongwaveLogbook?.Id, matchingLogbook.Id, StringComparison.Ordinal))
        {
            SelectedLongwaveLogbook = matchingLogbook;
        }

        LongwaveLogOperatorCallsign = contact.OperatorCallsign;
        LongwaveLogCallsign = contact.StationCallsign;
        LongwaveLogQsoDate = contact.QsoDate;
        LongwaveLogTimeOn = contact.TimeOn;
        LongwaveLogMode = contact.Mode;
        LongwaveLogBand = contact.Band;
        LongwaveLogFrequencyKhz = $"{contact.FrequencyKhz:0.0}";
        LongwaveLogRstSent = contact.RstSent ?? string.Empty;
        LongwaveLogRstReceived = contact.RstReceived ?? string.Empty;
        LongwaveLogParkReference = contact.ParkReference ?? string.Empty;
        LongwaveLogGridSquare = contact.GridSquare ?? string.Empty;
        LongwaveLogName = contact.Name ?? string.Empty;
        LongwaveLogQth = contact.Qth ?? string.Empty;
        LongwaveLogCounty = contact.County ?? string.Empty;
        LongwaveLogState = contact.State ?? string.Empty;
        LongwaveLogCountry = contact.Country ?? string.Empty;
        LongwaveLogDxcc = contact.Dxcc ?? string.Empty;
        _longwaveLogLatitude = contact.Latitude;
        _longwaveLogLongitude = contact.Longitude;
        LongwaveLogStatus = $"Loaded {contact.StationCallsign} for editing. {contact.QrzUploadText}.";
    }

    private void EnsureLongwaveLogbookSelected(LongwaveLogbook logbook)
    {
        var existing = LongwaveLogbooks.FirstOrDefault(item => string.Equals(item.Id, logbook.Id, StringComparison.Ordinal));
        if (existing is null)
        {
            existing = ToLongwaveLogbookItem(logbook);
            LongwaveLogbooks.Insert(0, existing);
        }

        SelectedLongwaveLogbook = existing;
    }

    private void UpsertLongwaveRecentContact(LongwaveContact contact)
    {
        var item = ToLongwaveRecentContactItem(contact);
        var existing = LongwaveRecentContacts.FirstOrDefault(existing => string.Equals(existing.Id, item.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            LongwaveRecentContacts.Remove(existing);
        }

        LongwaveRecentContacts.Insert(0, item);
        SelectedLongwaveRecentContact = item;
    }

    private void MarkLongwaveSpotLogged(string? sourceSpotId)
    {
        if (string.IsNullOrWhiteSpace(sourceSpotId))
        {
            return;
        }

        for (var i = 0; i < LongwavePotaSpots.Count; i++)
        {
            var item = LongwavePotaSpots[i];
            if (!string.Equals(item.Id, sourceSpotId, StringComparison.Ordinal))
            {
                continue;
            }

            var updated = item with { IsLogged = true };
            LongwavePotaSpots[i] = updated;
            if (SelectedLongwavePotaSpot?.Id == updated.Id)
            {
                SelectedLongwavePotaSpot = updated;
            }
            if (SelectedVoiceLongwavePotaSpot?.Id == updated.Id)
            {
                SelectedVoiceLongwavePotaSpot = updated;
            }
            break;
        }

        RebuildVoiceLongwavePotaSpots();
    }

    private void RebuildVoiceLongwavePotaSpots()
    {
        var bandFilter = SelectedVoiceLongwaveBandFilter;
        VoiceLongwavePotaSpots = new ObservableCollection<LongwaveSpotSummaryItem>(
            LongwavePotaSpots
                .Where(static spot => IsVoiceSpotMode(spot.Mode))
                .Where(spot => string.Equals(bandFilter, "All bands", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(spot.Band, bandFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static spot => spot.IsLogged)
                .ThenByDescending(static spot => spot.SpottedAtUtc));

        if (SelectedVoiceLongwavePotaSpot is not null)
        {
            SelectedVoiceLongwavePotaSpot = VoiceLongwavePotaSpots.FirstOrDefault(item => item.Id == SelectedVoiceLongwavePotaSpot.Id);
        }
    }

    private async Task RefreshLongwaveContactsAsync()
    {
        if (_longwaveService is null)
        {
            return;
        }

        var contacts = await _longwaveService.GetContactsAsync(BuildCurrentLongwaveSettings(), SelectedLongwaveLogbook?.Id, CancellationToken.None);
        LongwaveRecentContacts = new ObservableCollection<LongwaveRecentContactItem>(
            contacts.Take(50).Select(ToLongwaveRecentContactItem));
        OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
        if (SelectedLongwaveRecentContact is not null)
        {
            SelectedLongwaveRecentContact = LongwaveRecentContacts.FirstOrDefault(item => item.Id == SelectedLongwaveRecentContact.Id);
        }
    }

    private async Task RefreshLongwaveContactsForSelectionAsync()
    {
        try
        {
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
    }

    private async Task OnLongwaveRefreshTimerTickAsync()
    {
        if (_longwaveService is null || IsLongwaveBusy)
        {
            return;
        }

        var settings = BuildCurrentLongwaveSettings();
        if (!settings.Enabled
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ClientApiToken))
        {
            return;
        }

        try
        {
            await RefreshLongwaveSpotsAsync();
        }
        catch
        {
        }
    }

    private static string BuildLongwaveLogDedupeKey(string stationCall, string mode, double frequencyKhz, DateTime timestampUtc) =>
        $"{stationCall}|{mode.Trim().ToUpperInvariant()}|{Math.Round(frequencyKhz, 1):0.0}|{timestampUtc:yyyyMMddHHmm}";

    private static LongwaveLogbookItem? SelectPreferredLongwaveLogbook(
        IEnumerable<LongwaveLogbookItem> available,
        LongwaveLogbookItem? currentSelection,
        string preferredName)
    {
        var items = available.ToArray();
        if (currentSelection is not null)
        {
            var currentMatch = items.FirstOrDefault(item => string.Equals(item.Id, currentSelection.Id, StringComparison.Ordinal));
            if (currentMatch is not null)
            {
                return currentMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = items.FirstOrDefault(item => string.Equals(item.Name, preferredName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return items.FirstOrDefault();
    }

    private static RadioMode MapSpotModeToRadioMode(string mode, long frequencyHz)
    {
        var upper = mode.Trim().ToUpperInvariant();
        return upper switch
        {
            "FT8" or "FT4" or "WSPR" => RadioMode.UsbData,
            "RTTY" => RadioMode.Rtty,
            "CW" => RadioMode.Cw,
            "AM" => RadioMode.Am,
            "FM" => RadioMode.Fm,
            "SSTV" => frequencyHz < 10_000_000 ? RadioMode.LsbData : RadioMode.UsbData,
            "SSB" => frequencyHz < 10_000_000 ? RadioMode.Lsb : RadioMode.Usb,
            "USB" => RadioMode.Usb,
            "LSB" => RadioMode.Lsb,
            _ => frequencyHz < 10_000_000 ? RadioMode.Lsb : RadioMode.Usb,
        };
    }


    private static bool IsVoiceSpotMode(string mode) =>
        string.Equals(mode, "SSB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "USB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "LSB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "FM", StringComparison.OrdinalIgnoreCase);


    private static string MapRadioModeToLogMode(RadioMode mode) => mode switch
    {
        RadioMode.Lsb or RadioMode.Usb => "SSB",
        RadioMode.LsbData or RadioMode.UsbData => "DATA",
        RadioMode.Cw => "CW",
        RadioMode.Am => "AM",
        RadioMode.Fm => "FM",
        RadioMode.Rtty => "RTTY",
        _ => "SSB",
    };

    private static string MapRadioModeToPotaMode(RadioMode mode) => mode switch
    {
        RadioMode.Lsb or RadioMode.Usb => "SSB",
        RadioMode.LsbData or RadioMode.UsbData => "DATA",
        RadioMode.Cw => "CW",
        RadioMode.Am => "AM",
        RadioMode.Fm => "FM",
        RadioMode.Rtty => "RTTY",
        _ => "SSB",
    };

    private static string DeriveBandFromFrequencyKhz(double frequencyKhz)
    {
        if (frequencyKhz is >= 1800 and < 2000) return "160m";
        if (frequencyKhz is >= 3500 and < 4000) return "80m";
        if (frequencyKhz is >= 7000 and < 7300) return "40m";
        if (frequencyKhz is >= 10100 and < 10150) return "30m";
        if (frequencyKhz is >= 14000 and < 14350) return "20m";
        if (frequencyKhz is >= 18068 and < 18168) return "17m";
        if (frequencyKhz is >= 21000 and < 21450) return "15m";
        if (frequencyKhz is >= 24890 and < 24990) return "12m";
        if (frequencyKhz is >= 28000 and < 29700) return "10m";
        if (frequencyKhz is >= 50000 and < 54000) return "6m";
        return "HF";
    }

    partial void OnSelectedLongwavePotaSpotChanged(LongwaveSpotSummaryItem? value)
    {
        if (value is null)
        {
            return;
        }

        LongwaveStatus = $"Selected {value.ActivatorCallsign} on {value.FrequencyText} {value.Mode}.";
    }

    partial void OnSelectedVoiceLongwavePotaSpotChanged(LongwaveSpotSummaryItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedLongwavePotaSpot = value;
        LongwaveStatus = $"Selected voice spot {value.ActivatorCallsign} on {value.FrequencyText} {value.Mode}.";
    }

    partial void OnSelectedVoiceLongwaveBandFilterChanged(string value) => RebuildVoiceLongwavePotaSpots();

    partial void OnSelectedLongwaveRecentContactChanged(LongwaveRecentContactItem? value)
    {
        if (value is null)
        {
            LongwaveEditingContactId = string.Empty;
            return;
        }

        ApplyLongwaveContactToEditor(value);
    }

    partial void OnSelectedLongwaveLogbookChanged(LongwaveLogbookItem? value)
    {
        if (value is null)
        {
            LongwaveRecentContacts = [];
            LongwaveSelectedLogbookName = string.Empty;
            LongwaveSelectedLogbookOperatorCallsign = string.Empty;
            LongwaveSelectedLogbookParkReference = string.Empty;
            LongwaveSelectedLogbookActivationDate = string.Empty;
            LongwaveSelectedLogbookNotes = string.Empty;
            OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
            OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
            OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
            OnPropertyChanged(nameof(LongwaveQuickLogSummary));
            return;
        }

        LongwaveSelectedLogbookName = value.Name;
        LongwaveSelectedLogbookOperatorCallsign = value.OperatorCallsign;
        LongwaveSelectedLogbookParkReference = value.ParkReference ?? string.Empty;
        LongwaveSelectedLogbookActivationDate = value.ActivationDate ?? string.Empty;
        LongwaveSelectedLogbookNotes = value.Notes ?? string.Empty;
        LongwaveLogStatus = $"Using Longwave logbook {value.Name}.";
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
        OnPropertyChanged(nameof(LongwaveQuickLogSummary));
        _ = RefreshLongwaveContactsForSelectionAsync();
    }

    partial void OnCurrentFrequencyHzChanged(long value)
    {
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        OnPropertyChanged(nameof(FreedvActiveFrequencyDisplay));
        _ = UpdateFreedvReporterFrequencyAsync();
    }

    partial void OnSettingsLongwaveEnabledChanged(bool value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveBaseUrlChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveClientApiTokenChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveDefaultLogbookNameChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveDefaultLogbookNotesChanged(string value) => ApplyLongwaveSettingsState(_settings);
}
