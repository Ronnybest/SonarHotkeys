using SteelSeriesAPI.Core;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;
using SteelSeriesAPI.Sonar.Models;

namespace SonarHotkeys;

/// <param name="Presets">The presets offered for each channel: favorites and the user's own.</param>
/// <param name="Favorites">The ids of the favorite presets of each channel, which are added to the channel automatically.</param>
public sealed record SonarInventory(Dictionary<SonarChannel, List<Choice>> Presets, Dictionary<SonarChannel, List<string>> Favorites,
    List<Choice> Outputs, List<Choice> Inputs);

public sealed record SwitchResult(SonarChannel Channel, string PresetId, string PresetName, string DeviceName)
{
    public string Summary => PresetName + (DeviceName.Length == 0 ? "" : " → " + DeviceName);
}

/// <summary>Sonar operations shared by every user interface. Each call uses its own client.</summary>
public sealed class SonarService(Func<string> language)
{
    private static readonly Channel[] OutputChannels = [Channel.Game, Channel.Chat, Channel.Media, Channel.Aux];

    private string T(string key, params object[] arguments) => TextCatalog.Get(key, language(), arguments);

    private static Channel ToSonar(SonarChannel channel) => channel switch
    {
        SonarChannel.Game => Channel.Game,
        SonarChannel.Chat => Channel.Chat,
        SonarChannel.Media => Channel.Media,
        SonarChannel.Aux => Channel.Aux,
        _ => Channel.Mic,
    };

    /// <summary>Reads the presets worth offering for each channel (favorites and the user's own) and the physical devices.</summary>
    public async Task<SonarInventory> DiscoverAsync(CancellationToken ct)
    {
        using var sonar = new SonarClient();
        var configs = await sonar.Configs.GetAllAsync(ct);
        var presets = Enum.GetValues<SonarChannel>().ToDictionary(channel => channel, channel => configs
            .Where(c => c.Channel == ToSonar(channel) && (c.IsFavorite || !c.IsPreset))
            .Select(c => new Choice(c.Id, c.Name)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        static List<Choice> Sorted(IEnumerable<AudioDevice> devices) =>
            [.. devices.Select(d => new Choice(d.Id, d.Name)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
        var favoriteIds = configs.Where(c => c.IsFavorite).Select(c => c.Id).ToHashSet();
        var favorites = presets.ToDictionary(p => p.Key, p => p.Value.Where(c => favoriteIds.Contains(c.Id)).Select(c => c.Id).ToList());
        return new(presets, favorites,
            Sorted(await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, ct)),
            Sorted(await sonar.Devices.GetAllAsync(AudioDataFlow.Capture, false, ct)));
    }

    /// <summary>The id of the preset selected in each channel, read from Sonar in one request.</summary>
    public async Task<Dictionary<SonarChannel, string>> SelectedPresetsAsync(CancellationToken ct)
    {
        using var sonar = new SonarClient();
        var selected = await sonar.Configs.GetSelectedAsync(ct);
        return Enum.GetValues<SonarChannel>().Where(c => selected.ContainsKey(ToSonar(c)))
            .ToDictionary(c => c, c => selected[ToSonar(c)].Id);
    }

    /// <summary>The name of the preset selected in a channel, read from Sonar.</summary>
    public async Task<string?> SelectedPresetAsync(SonarChannel channel, CancellationToken ct)
    {
        using var sonar = new SonarClient();
        return (await sonar.Configs.GetSelectedAsync(ToSonar(channel), ct))?.Name;
    }

    public async Task<SwitchResult> ApplyAsync(SonarChannel channel, PresetBinding binding, bool allChannels, CancellationToken ct)
    {
        using var sonar = new SonarClient();
        return await ApplyAsync(sonar, channel, binding, allChannels, ct);
    }

    /// <summary>Selects the configured preset after the one currently selected in GG.</summary>
    public async Task<SwitchResult> CycleAsync(SonarChannel channel, IReadOnlyList<PresetBinding> bindings, bool allChannels, CancellationToken ct)
    {
        using var sonar = new SonarClient();
        // Read the actual selection, including changes made directly in GG.
        var available = (await sonar.Configs.GetAllAsync(ToSonar(channel), ct)).Select(c => c.Id).ToHashSet();
        var usable = bindings.Where(b => available.Contains(b.PresetId)).ToList();
        if (usable.Count == 0) throw new InvalidOperationException(T("Switch.NoConfiguredPresets", T(AppSettings.ChannelKey(channel))));
        var current = await sonar.Configs.GetSelectedAsync(ToSonar(channel), ct);
        var next = usable[(usable.FindIndex(b => b.PresetId == current?.Id) + 1) % usable.Count];
        return await ApplyAsync(sonar, channel, next, allChannels, ct);
    }

    private async Task<SwitchResult> ApplyAsync(SonarClient sonar, SonarChannel channel, PresetBinding binding, bool allChannels, CancellationToken ct)
    {
        var target = (await sonar.Configs.GetAllAsync(ToSonar(channel), ct)).FirstOrDefault(c => c.Id == binding.PresetId)
            ?? throw new InvalidOperationException(T("Switch.PresetMissing", T(AppSettings.ChannelKey(channel))));
        var undo = new Stack<Func<CancellationToken, Task>>();
        var previous = await sonar.Configs.GetSelectedAsync(ToSonar(channel), ct);
        string deviceName = "";
        try
        {
            if (!string.IsNullOrEmpty(binding.DeviceId))
            {
                bool output = AppSettings.IsOutput(channel);
                var device = (await sonar.Devices.GetAllAsync(output ? AudioDataFlow.Render : AudioDataFlow.Capture, false, ct))
                    .FirstOrDefault(d => d.Id == binding.DeviceId)
                    ?? throw new InvalidOperationException(T(output ? "Switch.OutputMissing" : "Switch.InputMissing"));
                deviceName = device.Name;
                if (await sonar.Mode.GetAsync(ct) == Mode.Classic)
                {
                    Channel[] channels = !output ? [Channel.Mic] : allChannels ? OutputChannels : [ToSonar(channel)];
                    var routes = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    foreach (Channel routed in channels)
                    {
                        var old = routes.FirstOrDefault(r => r.Channel == routed)
                            ?? throw new InvalidOperationException(T("Switch.ChannelDeviceMissing", routed));
                        undo.Push(token => sonar.Redirections.SetClassicDeviceAsync(routed, old.DeviceId, token));
                        await sonar.Redirections.SetClassicDeviceAsync(routed, device.Id, ct);
                    }
                    var confirmed = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    if (channels.Any(c => !confirmed.Any(r => r.Channel == c && r.DeviceId == device.Id)))
                        throw new InvalidOperationException(T("Switch.DeviceNotConfirmed"));
                }
                else
                {
                    // Streamer mode has no per-channel outputs: every output channel plays through the Personal mix.
                    var routes = await sonar.Redirections.GetStreamRedirectionsAsync(ct);
                    if (output)
                    {
                        var old = routes.Personal ?? throw new InvalidOperationException(T("Switch.PersonalMissing"));
                        undo.Push(token => sonar.Redirections.SetMixDeviceAsync(Mix.Personal, old.DeviceId, token));
                        await sonar.Redirections.SetMixDeviceAsync(Mix.Personal, device.Id, ct);
                    }
                    else
                    {
                        var old = routes.Mic ?? throw new InvalidOperationException(T("Switch.MicMissing"));
                        undo.Push(token => sonar.Redirections.SetMicDeviceAsync(old.DeviceId, token));
                        await sonar.Redirections.SetMicDeviceAsync(device.Id, ct);
                    }
                    var confirmed = await sonar.Redirections.GetStreamRedirectionsAsync(ct);
                    if ((output ? confirmed.Personal?.DeviceId : confirmed.Mic?.DeviceId) != device.Id)
                        throw new InvalidOperationException(T("Switch.DeviceNotConfirmed"));
                }
            }
            if (previous != null) undo.Push(token => sonar.Configs.SelectAsync(previous.Id, token));
            await sonar.Configs.SelectAsync(target.Id, ct);
            if ((await sonar.Configs.GetSelectedAsync(ToSonar(channel), ct))?.Id != target.Id)
                throw new InvalidOperationException(T("Switch.PresetNotConfirmed"));
        }
        catch (Exception ex)
        {
            if (undo.Count == 0) throw;
            bool restored = true;
            using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (undo.TryPop(out var restore))
                try { await restore(rollback.Token); } catch { restored = false; }
            throw new InvalidOperationException(T(restored ? "Switch.Restored" : "Switch.RestoreFailed", ErrorText(ex)), ex);
        }
        return new(channel, target.Id, target.Name, deviceName);
    }

    public string ErrorText(Exception ex) => ex switch
    {
        SteelSeriesNotFoundException => T("Error.GgNotFound"),
        SonarNotRunningException => T("Error.SonarNotRunning"),
        DiscoveryException => T("Error.Discovery", ex.Message),
        SonarWrongModeException => T("Error.WrongMode", ex.Message),
        OperationCanceledException => T("Error.Timeout"),
        HttpRequestException => T("Error.Connection", ex.Message),
        _ => ex.Message
    };
}
