using System.Text;
using Content.Server._RMC14.Language.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.CMU14.Language;
using Content.Server.CMU14.Platoon;
using Content.Server.CMU14.Roles;
using Content.Shared._RMC14.Language.Prototypes;
using Content.Shared._RMC14.Language.Systems;
using Content.Shared._RMC14.Marines;
using Content.Shared.CMU14.Round.Roles;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.CMU14.Chat;

public sealed partial class CMUApolloAnnouncementSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private CMUCharacterLanguageSystem _characters = default!;
    [Dependency] private RoundJobProfileSystem _jobs = default!;
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private PlatoonLanguageSystem _platoons = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IReplayRecordingManager _replay = default!;

    private const string Colony = "colony";

    public ProtoId<LanguagePrototype> ColonyLanguage =>
        _characters.GetColonyLanguages()?.DefaultLanguage ?? SharedLanguageSystem.CommonLanguage;

    public string? PresenceFaction(EntityUid crew, JobPrototype? job)
    {
        var side = _jobs.GetRoundSide(job);
        if (job != null && CMUCharacterLanguageSystem.IsColonyJob(job, side))
            return Colony;

        return side switch
        {
            RoundJobSide.Govfor => "govfor",
            RoundJobSide.Opfor => "opfor",
            _ => CompOrNull<MarineComponent>(crew)?.Faction,
        };
    }

    public ProtoId<LanguagePrototype> RadioLanguage(RadioChannelPrototype channel, string? crewFaction)
    {
        var faction = channel.Faction switch
        {
            "govfor" or "opfor" or "clf" => channel.Faction,
            _ when channel.ID is "Colony" or "colonyAlert" or "radioCMB" or "radioWEYU" or "ColonyHandheld" => Colony,
            _ => crewFaction ?? "govfor",
        };
        return FactionLanguage(faction);
    }

    private ProtoId<LanguagePrototype> FactionLanguage(string? faction)
    {
        faction = faction?.Trim().ToLowerInvariant();
        if (faction is Colony or "clf")
            return ColonyLanguage;

        if (faction is "marine" or null)
            faction = "govfor";

        var platoon = _platoons.GetFactionPlatoon(faction);
        return platoon == null
            ? SharedLanguageSystem.CommonLanguage
            : PlatoonLanguageSystem.GetDefaultLanguage(platoon) ?? SharedLanguageSystem.CommonLanguage;
    }

    private ProtoId<LanguagePrototype> PlatoonBroadcastLanguage(EntityUid? listener, string? faction)
    {
        if (string.IsNullOrWhiteSpace(faction))
            faction = CompOrNull<MarineComponent>(listener)?.Faction;

        return FactionLanguage(faction);
    }

    public void SendChat(Filter filter, string message, Func<string, string> wrap,
        EntityUid? source = null, Color? color = null, bool richText = false, bool recordReplay = true,
        bool usePlatoonLanguage = false, string? faction = null)
    {
        var colonyLanguage = ColonyLanguage;
        var clear = wrap(message);
        foreach (var session in filter.Recipients)
        {
            var listenerEntity = session.AttachedEntity;
            var language = usePlatoonLanguage ? PlatoonBroadcastLanguage(listenerEntity, faction) : colonyLanguage;
            var prototype = _prototypes.Index(language);
            var heard = session.AttachedEntity is { } listener
                ? Obfuscate(listener, message, language, source ?? EntityUid.Invalid, richText)
                : message;
            _chat.ChatMessageToOne(ChatChannel.Radio, heard, heard == message ? clear : wrap(heard),
                source ?? EntityUid.Invalid, false, session.Channel, colorOverride: color,
                languageIcon: prototype.HideLanguageName ? null : prototype.DisplayedLanguageIcon);
        }

        if (recordReplay)
        {
            var prototype = _prototypes.Index(usePlatoonLanguage ? FactionLanguage(faction) : colonyLanguage);
            _replay.RecordServerMessage(new ChatMessage(ChatChannel.Radio, message, clear,
                GetNetEntity(source ?? EntityUid.Invalid), null, colorOverride: color,
                languageIcon: (!usePlatoonLanguage || !string.IsNullOrWhiteSpace(faction)) && !prototype.HideLanguageName
                    ? prototype.DisplayedLanguageIcon
                    : null));
        }
    }

    public void AnnounceColony(string message, string sender, SoundSpecifier sound, EntityUid? source = null)
    {
        var filter = ColonyAnnouncements.Recipients(EntityManager);
        SendChat(filter, message, heard => Loc.GetString("chat-manager-sender-announcement-wrap-message",
            ("sender", sender), ("message", FormattedMessage.EscapeText(heard))), source);
        _audio.PlayGlobal(sound, filter, true, AudioParams.Default.WithVolume(-2f));
        _adminLogs.Add(LogType.Chat, LogImpact.Low, $"Station Announcement from {sender}: {message}");
    }

    public string[] OverlayLines(EntityUid? listener, string[] lines, EntityUid? source, bool usePlatoonLanguage = false)
    {
        if (listener is not { } entity)
            return lines;

        var language = usePlatoonLanguage ? PlatoonBroadcastLanguage(listener, null) : ColonyLanguage;
        if (_language.CanUnderstand(entity, language))
            return lines;

        var heard = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            heard[i] = Obfuscate(entity, lines[i], language, source ?? EntityUid.Invalid, richText: true);
        return heard;
    }

    internal string Obfuscate(EntityUid listener, string text, ProtoId<LanguagePrototype> language,
        EntityUid source, bool richText)
    {
        if (!richText)
            return _language.ObfuscateMessageForListener(listener, text, language, source);
        if (_language.CanUnderstand(listener, language))
            return text;

        // Keep formatting tags intact and obfuscate only the visible text.
        var parsed = FormattedMessage.FromMarkupPermissive(text);
        var result = new StringBuilder();
        foreach (var node in parsed.Nodes)
        {
            if (node.Name == null && node.Value.StringValue is { } value)
                result.Append(FormattedMessage.EscapeText(_language.ObfuscateMessageForListener(listener, value, language, source)));
            else
                result.Append(node);
        }
        return result.ToString();
    }
}
