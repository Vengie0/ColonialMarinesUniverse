using Content.Server.CMU14.Chat; // CMU14
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Station.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private CMUApolloAnnouncementSystem _colonyAnnouncements = default!; // CMU14

    /// <inheritdoc />
    public override void DispatchGlobalAnnouncement(
        string message,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null,
        bool useColonyLanguage = true // CMU14
        )
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        // CMU14 Begin: in-world broadcasts use the current colony's language.
        if (useColonyLanguage)
            SendColonyAnnouncement(Filter.Broadcast(), message, sender, null, colorOverride);
        else
            _chatManager.ChatMessageToAll(ChatChannel.Radio, message, WrapAnnouncement(message, sender), default, false, true, colorOverride);
        // CMU14 End
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, Filter.Broadcast(), true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {sender}: {message}");
    }

    /// <inheritdoc />
    public override void DispatchFilteredAnnouncement(
        Filter filter,
        string message,
        EntityUid? source = null,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        SendColonyAnnouncement(filter, message, sender, source, colorOverride); // CMU14
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement from {sender}: {message}");
    }

    /// <inheritdoc />
    public override void DispatchStationAnnouncement(
        EntityUid source,
        string message,
        string? sender = null,
        bool playDefaultSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var station = _stationSystem.GetOwningStation(source);

        if (station == null)
        {
            // you can't make a station announcement without a station
            return;
        }

        if (!TryComp<StationDataComponent>(station, out var stationDataComp)) return;

        var filter = _stationSystem.GetInStation(stationDataComp);

        SendColonyAnnouncement(filter, message, sender, source, colorOverride); // CMU14

        if (playDefaultSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement on {station} from {sender}: {message}");
    }
    // CMU14 methods: preserve the normal header and escape announcement text.
    private void SendColonyAnnouncement(Filter filter, string message, string sender, EntityUid? source, Color? color)
        => _colonyAnnouncements.SendChat(filter, message, heard => WrapAnnouncement(heard, sender), source, color);

    private string WrapAnnouncement(string message, string sender)
        => Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender),
            ("message", FormattedMessage.EscapeText(message)));
}
