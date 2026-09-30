using SteelSeriesAPI.Core;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;

namespace SonarHotkeys;

public sealed record SonarInventory(List<Choice> Favorites, List<Choice> Devices);

public sealed record SwitchResult(string PresetId, string PresetName, string DeviceName)
{
    public string Summary => PresetName + (DeviceName.Length == 0 ? "" : " → " + DeviceName);
}

/// <summary>Sonar operations shared by every user interface. Each call uses its own client.</summary>
public sealed class SonarService(Func<string> language)
{
    private static readonly Channel[] ClassicChannels = [Channel.Game, Channel.Chat, Channel.Media, Channel.Aux];

    private string T(string key, params object[] arguments) => TextCatalog.Get(key, language(), arguments);

    public async Task<SonarInventory> DiscoverAsync(CancellationToken ct)
    {
        using var sonar = new SonarClient();
        var configs = await sonar.Configs.GetAllAsync(Channel.Game, ct);
        var devices = await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, ct);
        return new(configs.Where(c => c.IsFavorite).Select(c => new Choice(c.Id, c.Name)).OrderBy(c => c.Name).ToList(),
            devices.Select(d => new Choice(d.Id, d.Name)).OrderBy(d => d.Name).ToList());
    }

    public async Task<SwitchResult> ApplyAsync(PresetBinding binding, CancellationToken ct)
    {
        using var sonar = new SonarClient();
        return await ApplyAsync(sonar, binding, ct);
    }

    /// <summary>Selects the configured favorite after the one currently selected in GG.</summary>
    public async Task<SwitchResult> CycleAsync(IReadOnlyList<PresetBinding> bindings, CancellationToken ct)
    {
        using var sonar = new SonarClient();
        // Read the actual selection, including changes made directly in GG.
        var available = (await sonar.Configs.GetAllAsync(Channel.Game, ct)).Where(c => c.IsFavorite).ToDictionary(c => c.Id);
        var favorites = bindings.Select(b => b.PresetId).Distinct()
            .Where(available.ContainsKey).Select(id => available[id]).ToList();
        if (favorites.Count == 0) throw new InvalidOperationException(T("Switch.NoConfiguredFavorites"));
        var current = await sonar.Configs.GetSelectedAsync(Channel.Game, ct);
        var next = favorites[(favorites.FindIndex(c => c.Id == current?.Id) + 1) % favorites.Count];
        var binding = bindings.FirstOrDefault(b => b.PresetId == next.Id)
            ?? new PresetBinding { PresetId = next.Id };
        return await ApplyAsync(sonar, binding, ct);
    }

    private async Task<SwitchResult> ApplyAsync(SonarClient sonar, PresetBinding binding, CancellationToken ct)
    {
        var configs = await sonar.Configs.GetAllAsync(Channel.Game, ct);
        var target = configs.FirstOrDefault(c => c.Id == binding.PresetId)
            ?? throw new InvalidOperationException(T("Switch.PresetMissing"));
        var undo = new Stack<Func<CancellationToken, Task>>();
        var previous = await sonar.Configs.GetSelectedAsync(Channel.Game, ct);
        string deviceName = "";
        try
        {
            if (!string.IsNullOrEmpty(binding.DeviceId))
            {
                var devices = await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, ct);
                var device = devices.FirstOrDefault(d => d.Id == binding.DeviceId)
                    ?? throw new InvalidOperationException(T("Switch.DeviceMissing"));
                deviceName = device.Name;
                var mode = await sonar.Mode.GetAsync(ct);
                if (mode == Mode.Classic)
                {
                    var routes = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    foreach (Channel channel in ClassicChannels)
                    {
                        var old = routes.FirstOrDefault(r => r.Channel == channel)
                            ?? throw new InvalidOperationException(T("Switch.ChannelDeviceMissing", channel));
                        undo.Push(token => sonar.Redirections.SetClassicDeviceAsync(channel, old.DeviceId, token));
                        await sonar.Redirections.SetClassicDeviceAsync(channel, device.Id, ct);
                    }
                    var confirmed = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    if (ClassicChannels.Any(c => !confirmed.Any(r => r.Channel == c && r.DeviceId == device.Id)))
                        throw new InvalidOperationException(T("Switch.DeviceNotConfirmed"));
                }
                else
                {
                    var old = (await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal
                        ?? throw new InvalidOperationException(T("Switch.PersonalMissing"));
                    undo.Push(token => sonar.Redirections.SetMixDeviceAsync(Mix.Personal, old.DeviceId, token));
                    await sonar.Redirections.SetMixDeviceAsync(Mix.Personal, device.Id, ct);
                    if ((await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal?.DeviceId != device.Id)
                        throw new InvalidOperationException(T("Switch.PersonalNotConfirmed"));
                }
            }
            if (previous != null) undo.Push(token => sonar.Configs.SelectAsync(previous.Id, token));
            await sonar.Configs.SelectAsync(target.Id, ct);
            if ((await sonar.Configs.GetSelectedAsync(Channel.Game, ct))?.Id != target.Id)
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
        return new(target.Id, target.Name, deviceName);
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
