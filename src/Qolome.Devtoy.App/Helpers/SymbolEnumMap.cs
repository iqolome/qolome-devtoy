using Microsoft.UI.Xaml.Controls;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// WinUI <see cref="Symbol"/> 与 Segoe Fluent 显示码位对照（New UniCodePoint）。
/// 仅保留当前 SDK 中实际存在的枚举成员。
/// </summary>
public static class SymbolEnumMap
{
    private static readonly (string Name, int DisplayCodePoint)[] Spec =
    [
        ("Accept", 0xE8FB),
        ("Account", 0xE910),
        ("Add", 0xE710),
        ("AddFriend", 0xE8FA),
        ("Admin", 0xE7EF),
        ("AlignCenter", 0xE8E3),
        ("AlignLeft", 0xE8E4),
        ("AlignRight", 0xE8E2),
        ("AllApps", 0xE71D),
        ("Attach", 0xE723),
        ("AttachCamera", 0xE8A2),
        ("Audio", 0xE8D6),
        ("Back", 0xE72B),
        ("BackToWindow", 0xE73F),
        ("BlockContact", 0xE8F8),
        ("Bold", 0xE8DD),
        ("Bookmarks", 0xE8A4),
        ("BrowsePhotos", 0xE7C5),
        ("Bullets", 0xE8FD),
        ("Calculator", 0xE8EF),
        ("Calendar", 0xE787),
        ("CalendarDay", 0xE8BF),
        ("CalendarReply", 0xE8F5),
        ("CalendarWeek", 0xE8C0),
        ("Camera", 0xE722),
        ("Cancel", 0xE711),
        ("Caption", 0xE8BA),
        ("CellPhone", 0xE8EA),
        ("Character", 0xE8C1),
        ("Clear", 0xE894),
        ("ClearSelection", 0xE8E6),
        ("Clock", 0xE823),
        ("ClosedCaption", 0xE7F0),
        ("ClosePane", 0xE89F),
        ("Comment", 0xE90A),
        ("Contact", 0xE77B),
        ("Contact2", 0xE8D4),
        ("ContactInfo", 0xE779),
        ("ContactPresence", 0xE8CF),
        ("Copy", 0xE8C8),
        ("Crop", 0xE7A8),
        ("Cut", 0xE8C6),
        ("Delete", 0xE74D),
        ("Directions", 0xE8F0),
        ("DisableUpdates", 0xE8D8),
        ("DisconnectDrive", 0xE8CD),
        ("Dislike", 0xE8E0),
        ("DockBottom", 0xE90E),
        ("DockLeft", 0xE90C),
        ("DockRight", 0xE90D),
        ("Document", 0xE8A5),
        ("Download", 0xE896),
        ("Edit", 0xE70F),
        ("Emoji", 0xE899),
        ("Emoji2", 0xE76E),
        ("Favorite", 0xE734),
        ("Filter", 0xE71C),
        ("Find", 0xE721),
        ("Flag", 0xE7C1),
        ("Folder", 0xE8B7),
        ("Font", 0xE8D2),
        ("FontColor", 0xE8D3),
        ("FontDecrease", 0xE8E7),
        ("FontIncrease", 0xE8E8),
        ("FontSize", 0xE8E9),
        ("Forward", 0xE72A),
        ("FourBars", 0xE908),
        ("FullScreen", 0xE740),
        ("GlobalNavigationButton", 0xE700),
        ("Globe", 0xE774),
        ("Go", 0xE8AD),
        ("GoToStart", 0xE8FC),
        ("GoToToday", 0xE8D1),
        ("HangUp", 0xE778),
        ("Help", 0xE897),
        ("HideBcc", 0xE8C5),
        ("Highlight", 0xE7E6),
        ("Home", 0xE80F),
        ("Import", 0xE8B5),
        ("ImportAll", 0xE8B6),
        ("Important", 0xE8C9),
        ("Italic", 0xE8DB),
        ("Keyboard", 0xE765),
        ("LeaveChat", 0xE89B),
        ("Library", 0xE8F1),
        ("Like", 0xE8E1),
        ("LikeDislike", 0xE8DF),
        ("Link", 0xE71B),
        ("List", 0xEA37),
        ("Mail", 0xE715),
        ("MailFilled", 0xE8A8),
        ("MailForward", 0xE89C),
        ("MailReply", 0xE8CA),
        ("MailReplyAll", 0xE8C2),
        ("Manage", 0xE912),
        ("Map", 0xE707),
        ("MapDrive", 0xE8CE),
        ("MapPin", 0xE7B7),
        ("Memo", 0xE77C),
        ("Message", 0xE8BD),
        ("Microphone", 0xE720),
        ("More", 0xE712),
        ("MoveToFolder", 0xE8DE),
        ("MusicInfo", 0xE90B),
        ("Mute", 0xE74F),
        ("NewFolder", 0xE8F4),
        ("NewWindow", 0xE78B),
        ("Next", 0xE893),
        ("OneBar", 0xE905),
        ("OpenFile", 0xE8E5),
        ("OpenLocal", 0xE8DA),
        ("OpenPane", 0xE8A0),
        ("OpenWith", 0xE7AC),
        ("Orientation", 0xE8B4),
        ("OtherUser", 0xE7EE),
        ("OutlineStar", 0xE734),
        ("Page", 0xE729),
        ("Page2", 0xE7C3),
        ("Paste", 0xE77F),
        ("Pause", 0xE769),
        ("People", 0xE716),
        ("Permissions", 0xE8D7),
        ("Phone", 0xE717),
        ("PhoneBook", 0xE780),
        ("Pictures", 0xE8B9),
        ("Pin", 0xE718),
        ("Placeholder", 0xE18A),
        ("Play", 0xE768),
        ("PostUpdate", 0xE8F3),
        ("Preview", 0xE8FF),
        ("PreviewLink", 0xE8A1),
        ("Previous", 0xE892),
        ("Print", 0xE749),
        ("Priority", 0xE8D0),
        ("ProtectedDocument", 0xE8A6),
        ("Read", 0xE8C3),
        ("Redo", 0xE7A6),
        ("Refresh", 0xE72C),
        ("Remote", 0xE8AF),
        ("Remove", 0xE738),
        ("Rename", 0xE8AC),
        ("Repair", 0xE90F),
        ("RepeatAll", 0xE8EE),
        ("RepeatOne", 0xE8ED),
        ("ReportHacked", 0xE730),
        ("ReShare", 0xE8EB),
        ("Rotate", 0xE7AD),
        ("RotateCamera", 0xE89E),
        ("Save", 0xE74E),
        ("SaveLocal", 0xE78C),
        ("Scan", 0xE8FE),
        ("SelectAll", 0xE8B3),
        ("Send", 0xE724),
        ("SetLockScreen", 0xE7B5),
        ("SetTile", 0xE97B),
        ("Setting", 0xE713),
        ("Share", 0xE72D),
        ("Shop", 0xE719),
        ("ShowBcc", 0xE8C4),
        ("ShowResults", 0xE8BC),
        ("Shuffle", 0xE8B1),
        ("SlideShow", 0xE786),
        ("SolidStar", 0xE735),
        ("Sort", 0xE8CB),
        ("Stop", 0xE71A),
        ("StopSlideShow", 0xE620),
        ("Street", 0xE913),
        ("Switch", 0xE8AB),
        ("SwitchApps", 0xE8F9),
        ("Sync", 0xE895),
        ("SyncFolder", 0xE8F7),
        ("Tag", 0xE8EC),
        ("Target", 0xF5F0),
        ("ThreeBars", 0xE907),
        ("TouchPointer", 0xE7C9),
        ("Trim", 0xE78A),
        ("TwoBars", 0xE906),
        ("TwoPage", 0xE89A),
        ("Underline", 0xE8DC),
        ("Undo", 0xE7A7),
        ("UnFavorite", 0xE8D9),
        ("UnPin", 0xE77A),
        ("UnSyncFolder", 0xE8F6),
        ("Up", 0xE74A),
        ("Upload", 0xE898),
        ("Video", 0xE714),
        ("VideoChat", 0xE8AA),
        ("View", 0xE890),
        ("ViewAll", 0xE8A9),
        ("Volume", 0xE767),
        ("WebCam", 0xE8B8),
        ("World", 0xE909),
        ("XboxOneConsole", 0xE990),
        ("ZeroBars", 0xE904),
        ("Zoom", 0xE71E),
        ("ZoomIn", 0xE8A3),
        ("ZoomOut", 0xE71F),
    ];

    private static readonly Dictionary<int, string> CodePointToSymbol;
    private static readonly Dictionary<string, string> CatalogNameToSymbol;
    private static readonly HashSet<int> SymbolCodePoints;

    static SymbolEnumMap()
    {
        CodePointToSymbol = new Dictionary<int, string>();
        CatalogNameToSymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SymbolCodePoints = new HashSet<int>();

        foreach (var (name, displayCodePoint) in Spec)
        {
            if (!Enum.TryParse(name, ignoreCase: false, result: out Symbol _))
            {
                continue;
            }

            SymbolCodePoints.Add(displayCodePoint);
            // 同码位多个 Symbol 时保留首次（与文档表顺序一致）
            CodePointToSymbol.TryAdd(displayCodePoint, name);
            CatalogNameToSymbol[name] = name;
        }

        // 文档字形名与 Symbol 名不一致时的别名
        Alias("Settings", "Setting");
        Alias("GlobalNavButton", "GlobalNavigationButton");
        Alias("Search", "Find");
        Alias("FavoriteStar", "Favorite");
        Alias("FavoriteStarFill", "SolidStar");
    }

    private static void Alias(string catalogName, string symbolName)
    {
        if (Enum.TryParse(symbolName, ignoreCase: false, result: out Symbol _))
        {
            CatalogNameToSymbol[catalogName] = symbolName;
        }
    }

    public static bool IsSymbolCodePoint(int codePoint) => SymbolCodePoints.Contains(codePoint);

    public static string? TryGetSymbolName(string catalogName, int codePoint)
    {
        if (CatalogNameToSymbol.TryGetValue(catalogName, out var byName))
        {
            return byName;
        }

        return CodePointToSymbol.TryGetValue(codePoint, out var byCode) ? byCode : null;
    }

    public static int SymbolCount => SymbolCodePoints.Count;
}
