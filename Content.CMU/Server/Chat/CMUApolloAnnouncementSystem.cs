using System.Text;
using Content.Server._RMC14.Language.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.CMU14.Language;
using Content.Shared._RMC14.Language.Prototypes;
using Content.Shared._RMC14.Language.Systems;
using Content.Shared.Chat;
using Content.Shared.Database;
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
    [Dependency] private LanguageSystem _language = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IReplayRecordingManager _replay = default!;

    public ProtoId<LanguagePrototype> ColonyLanguage =>
        _characters.GetColonyLanguages()?.DefaultLanguage ?? SharedLanguageSystem.CommonLanguage;

    public void SendChat(Filter filter, string message, Func<string, string> wrap,
        EntityUid? source = null, Color? color = null, bool richText = false, bool recordReplay = true)
    {
        var language = ColonyLanguage;
        var prototype = _prototypes.Index(language);
        var clear = wrap(message);
        var languageIcon = prototype.HideLanguageName ? null : prototype.DisplayedLanguageIcon;
        foreach (var session in filter.Recipients)
        {
            var heard = session.AttachedEntity is { } listener
                ? Obfuscate(listener, message, language, source ?? EntityUid.Invalid, richText)
                : message;
            _chat.ChatMessageToOne(ChatChannel.Radio, heard, heard == message ? clear : wrap(heard),
                source ?? EntityUid.Invalid, false, session.Channel, colorOverride: color,
                languageIcon: languageIcon);
        }

        if (recordReplay)
        {
            _replay.RecordServerMessage(new ChatMessage(ChatChannel.Radio, message, clear,
                GetNetEntity(source ?? EntityUid.Invalid), null, colorOverride: color, languageIcon: languageIcon));
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

    public string[] OverlayLines(EntityUid? listener, string[] lines, EntityUid? source)
    {
        if (listener is not { } entity)
            return lines;

        var language = ColonyLanguage;
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
