# FrameFit — ארכיטקטורה

| | |
|---|---|
| **תאריך** | 2026-09-14 |
| **סטטוס** | ממומש. שכבות 1, 3 ו-4 אומתו בבדיקה עצמית; שכבה 2 מיושמת וטרם אומתה מול תוכן אמיתי |

---

## 1. עקרון מארגן

Windows לא חושף "שוליים מתים" ולא מאפשר להזיז את שולחן העבודה בתוך הפאנל. לכן האכיפה
נבנית ב**שלוש שכבות בלתי תלויות**, שכל אחת מהן נותנת כיסוי לחלק אחר מהבעיה, כך שאם שכבה
אחת נכשלת (למשל תוכנית שמתעלמת משטח העבודה) האחרות משלימות:

```
┌──────────────────────────────────────────────────────────────┐
│  שכבה 1 — שטח עבודה (AppBar)                                  │
│  מצמצם את rcWork של המסך ⇒ מקסם, סרגל משימות, Snap מכבדים     │
├──────────────────────────────────────────────────────────────┤
│  שכבה 2 — שומר מסך-מלא (WinEvent + התאמה)                    │
│  מזהה חלון שחורג מהאזור הגלוי ומתאים אותו                     │
├──────────────────────────────────────────────────────────────┤
│  שכבה 3 — ויזואל וקלט (Overlay + ClipCursor)                  │
│  השוליים שחורים, וסמן העכבר לא נכנס אליהם                     │
└──────────────────────────────────────────────────────────────┘
```

## 2. מבנה הפרויקט (כפי שנבנה)

```
FrameFit/
├── FrameFit.slnx
├── src/
│   ├── FrameFit.Core/                  # לוגיקה טהורה — ללא Win32, נבדקת ביחידה
│   │   ├── Geometry/                   #   PixelRect, MarginSet, VisibleAreaCalculator
│   │   ├── Profiles/                   #   DisplayFingerprint, ProfileMatcher, ProfileStore
│   │   ├── Abstractions/               #   DisplayInfo, IDisplayProvider
│   │   └── AppPaths.cs
│   ├── FrameFit.Platform.Windows/      # כל האינטראפ עם Win32
│   │   ├── Interop/                    #   Native.cs — כל ה-P/Invoke במקום אחד
│   │   │                               #   MessageWindow.cs — חלון הודעות
│   │   ├── Displays/                   #   מיפוי מסכים, סוג חיבור, EDID
│   │   ├── WorkArea/                   #   WorkAreaHost — AppBar (שכבה 1)
│   │   ├── Watching/                   #   FullscreenWatcher (שכבה 2)
│   │   ├── Overlay/                    #   OverlayHost + OverlayStrip (שכבה 3)
│   │   ├── Input/                      #   CursorClamp, GlobalHotKey
│   │   ├── Diagnostics/                #   DiagnosticsService, DisplayProbe
│   │   ├── Logging/AppLog.cs
│   │   └── AutoStart.cs
│   └── FrameFit.App/                   # WPF — ממשק בעברית RTL
│       ├── MainWindow.xaml(.cs)
│       ├── ViewModels/MainViewModel.cs
│       ├── Services/FrameFitSession.cs #   תיאום כל מנגנוני האכיפה
│       ├── Infrastructure/             #   BindableBase, RelayCommand, ConsoleBridge
│       ├── Tray/TrayIcon.cs
│       └── SelfTest.cs                 #   בדיקה עצמית של האינטראפ
├── tests/
│   └── FrameFit.Core.Tests/            # xUnit — 52 בדיקות
└── docs/
```

ההפרדה נשמרה: **כל הגיאומטריה והמודל ב-`Core`** — שם חיים כל חישובי האזור הגלוי
והבדיקות. `Platform.Windows` אחראי רק לבצע פעולות במערכת ההפעלה, ו-`App` מתאם ביניהם.

`FrameFit.Cli` לא נכתב; במקומו קיים `--tray` להרצה ברקע, ומצב `--self-test` לאבחון.

## 3. רכיבים

### 3.1 `DisplayService` (Platform)
* מיפוי מסכים: `EnumDisplayMonitors` + `GetMonitorInfo` (מחזיר `rcMonitor` ו-`rcWork`).
* זהות התצוגה: `QueryDisplayConfig` → `DISPLAYCONFIG_TARGET_DEVICE_NAME`
  נותן את שם ההתקן, `outputTechnology` (**HDMI / DisplayPort / VGA / Internal**) ומזהי EDID.
* זיהוי TV מול מוניטור: קריאת בלוק ה-EDID מהרישום
  (`HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\…\Device Parameters\EDID`) ובדיקת קיום
  הרחבת CEA-861 וסוג המוצר.
* גודל פיזי (להצגת מ"מ): מילות גודל התמונה ב-EDID.
* מודעות ל-DPI: התהליך יהיה **Per-Monitor DPI Aware V2**, וכל החישובים במיקום פיזי.

### 3.2 `WorkAreaHost` — שכבה 1 (Platform)
* רושם **סרגלי יישום (AppBars)** על ארבעת שולי המסך הנבחר דרך `SHAppBarMessage`
  (`ABM_NEW`, `ABM_SETPOS`, `ABM_REMOVE`), עם חלון מוסתר לכל צד.
* התוצאה: `rcWork` של אותו מסך קטן בהתאם — וזו המשמעות של "אזור העבודה" במערכת ההפעלה.
* **זהו נכס האכיפה החזק ביותר**, משום שהוא מכבד ע"י מנגנוני המערכת עצמם.
* ⚠️ **ספיק טכני חובה (שלב 0):** לאמת ש-Windows אכן מפחית את `rcWork` על ארבעה צדדים
  בו-זמנית, ואיך הדבר מתנהג עם כמה מסכים ועם שינוי רזולוציה. אם מתברר שאין תמיכה
  אמינה — נפילה חלופית: המרת חלונות ממוקסמים מחדש בלולאת ההתאמה (מכסה את אותה דרישה).

### 3.3 `FullscreenWatcher` — שכבה 2 (Platform)
* `SetWinEventHook` על `EVENT_OBJECT_LOCATIONCHANGE`, `EVENT_SYSTEM_FOREGROUND`,
  `EVENT_SYSTEM_MOVESIZEEND`, `EVENT_SYSTEM_MINIMIZESTART` — **out-of-context**, ללא הזרקה.
* לולאת התאמה (reconciler) בתדירות נמוכה (250–500 מ"ש) שגם מטפלת במקרים שפספסו ה-hooks,
  ומתרוקנת כשהמערכת יציבה (אין polling מיותר — ראו NFR-1).
* זיהוי "מסך-מלא": מלבן החלון מכסה את רוב מלבן המסך, ללא מסגרת (`WS_POPUP`/ללא כיתוב),
  או חורג מ-`rcWork`. התוצאה: `SetWindowPos` לגבולות האזור הגלוי.
* טיפול בשקיפות: חלונות על-שכבתיים (overlay) של FrameFit עצמו מסוננים לפי מחלקה/מזהה תהליך.

### 3.4 `OverlayHost` — שכבה 3 (Platform)
* ארבעה חלונות מלבניים (אופציונלי: חלון אחד עם אזור שקוף), בסגנון
  `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `TopMost`.
* עדכון גיאומטריה בליווי `SetWindowPos`/`SetLayeredWindowAttributes` בלבד.
* מצב "תצוגה מקדימה" (שקיפות חלקית כדי שהטכנאי יראה מה יש מתחת) ומצב "אכיפה" (שחור מלא).
* מאזין ל-`WM_DISPLAYCHANGE` ולדיווחי שינוי תצורה כדי לחדש גבולות אוטומטית.

### 3.5 `InputGuard` (Platform)
* `ClipCursor` לאזור הגלוי.
* `RegisterHotKey` למקש המילוט ול"שחזר הכול" — עובד גם כשהעכבר חסום.
* ההשפעה של `ClipCursor` היא ברמת הדסקטופ כל עוד התהליך פעיל; לכן **חייבים שחרור ביציאה**
  (וראו סעיף 6 — שחרור אוטומטי בעת סיום תהליך מתבצע ע"י Windows).

### 3.6 `ProfileStore` (Core)
* שמירה/טעינה JSON, מיזוג עם ברירות מחדל, ולידציה מלאה. קובץ פגום ⇒ החזרת ברירת מחדל
  "כבוי" ואירוע בלוג (FR-9).

### 3.7 `Diagnostics` (Platform)
* כרטיס מסך ודרייבר (WMI `Win32_VideoController`), סוג חיבור, TV/מוניטור, `rcWork` בפועל,
  רזולוציות זמינות, וזמינות מנוע הדרייבר. מפיק דוח טקסט להעתקה.

## 4. מודל הגיאומטריה

```
visibleRect.left   = monitor.left + margin.left
visibleRect.top    = monitor.top  + margin.top
visibleRect.width  = monitor.width  - margin.left - margin.right
visibleRect.height = monitor.height - margin.top  - margin.bottom
```

* כל הערכים ב**פיקסלים פיזיים** של המסך (לפני scale), בקואורדינטות דסקטופ וירטואלי.
* ולידציה: `width ≥ MIN_W` ו-`height ≥ MIN_H` (ברירת מחדל 640×480), וכל שוליים אי-שליליים.
* הצגת ערך במ"מ: `px / monitor.pixelsPerMm` כאשר הגודל הפיזי זמין מה-EDID.

## 5. סכימת פרופיל (JSON)

```json
{
  "schemaVersion": 1,
  "activeProfile": "display-primary",
  "settings": { "startWithWindows": true, "language": "he-IL" },
  "profiles": [
    {
      "id": "display-primary",
      "displayFingerprint": {
        "deviceName": "SAMSUNG-LS24",
        "edidHash": "9f3a1c...",
        "outputTechnology": "HDMI",
        "width": 1920,
        "height": 1080
      },
      "margins": { "left": 38, "right": 64, "top": 22, "bottom": 14 },
      "options": {
        "reserveWorkArea": true,
        "refitFullscreen": true,
        "blackoutMargins": true,
        "clipCursor": true,
        "hardLock": false
      }
    }
  ]
}
```

## 6. מחזור חיים ובטיחות

| שלב | פעולות |
|---|---|
| עלייה | טעינת פרופיל ← אימות תצוגה לפי טביעת אצבע ← החלה ← רישום מחדש |
| תצוגה מקדימה | Overlay בלבד, עם טיימר ביטול אוטומטי (ברירת מחדל 15 ש') |
| החלה | הפעלת שלוש השכבות לפי סדר 1→2→3, ושמירת "מצב קודם" לשחזור |
| שינוי תצורה | `WM_DISPLAYCHANGE` ⇒ עצירה, חישוב מחדש, החלה |
| יציאה / קריסה | `ABM_REMOVE` לכל סרגל, סגירת overlays, שחרור hotkeys. סיום תהליך משחרר גם `ClipCursor` |
| שחזור כשל | קובץ הגדרות פגום / תצוגה לא מזוהה ⇒ **לא מחילים כלום**, מדווחים בלוג |

מנגנון שמירה נוסף: הפעלה ראשונה של פרופיל חדש מוגדרת "ניסיון" — אם התהליך לא אישר את
ההחלה תוך פרק זמן, מוחזר המצב הקודם.

## 7. משטח האינטראפ עם Windows (ריכוז)

| API | שימוש | הערה |
|---|---|---|
| `EnumDisplayMonitors`, `GetMonitorInfo` | מיפוי מסכים, `rcMonitor`/`rcWork` | הליבה של כל חישוב |
| `QueryDisplayConfig`, `DISPLAYCONFIG_TARGET_DEVICE_NAME` | זהות תצוגה, סוג חיבור | עובד גם במסכים מרובים |
| `SHAppBarMessage` | הזמנת שוליים ⇒ הקטנת `rcWork` | **טעון ספיק (שלב 0)** |
| `SetWinEventHook` | מעקב אחרי חלונות | out-of-context, ללא הזרקה |
| `SetWindowPos`, `GetWindowRect`, `GetWindowLongPtr` | התאמת חלונות | דורש לוגיקת סינון מדויקת |
| `SetLayeredWindowAttributes`, `WS_EX_TRANSPARENT` | שכבת כיסוי עיוורת לקלט | |
| `ClipCursor` | חסימת סמן העכבר | דסקטופ-רחב בזמן פעילות התהליך |
| `RegisterHotKey` | מקש מילוט/שחזור | עובד למרות חסימת העכבר |
| `WM_DISPLAYCHANGE`, `WM_DPICHANGED` | שינויי תצורה בזמן ריצה | |
| `WMI Win32_VideoController` | זיהוי כרטיס מסך ודרייבר | לצורך אבחון ומנוע דרייבר |

### לקחים שנאספו במימוש

**גודל שגוי של מבנה אינטראפ הוא כשל שקט.** ב-P/Invoke, מבנה בגודל שגוי אינו זורק חריגה
ואינו מפיל את התוכנית — הוא פשוט מחזיר כשלון מהמערכת. בפועל, חסר `CharSet.Unicode` על
`MONITORINFOEX` גרם למאשרל לתקצב את שדה המחרוזת כ-ANSI, לגודל 72 במקום 104, ול-
`GetMonitorInfoW` להיכשל בשגיאה 87 — ומיפוי המסכים החזיר **אפס מסכים** בלי שום סימן.

לכן נוספה בדיקה קבועה שמאמתת את הגודל של כל שמונה מבני האינטראפ, והיא **הבדיקה הראשונה**
שרצה בכל בדיקה עצמית (`DisplayProbe.StructSizeChecks`). כל שינוי עתידי בהצהרות ה-P/Invoke
ייתפס מיד.

**פער בין מזהי התקן לבין הרישום.** `EnumDisplayDevices` מחזיר מזהה מופע לוגי
(`MONITOR\AUO208D\{guid}\0000`) שאינו המזהה שבו נשמרת הרשומה תחת `Enum\DISPLAY`. לכן
קריאת ה-EDID חייבת לאתר את הרשומה לפי **מזהה החומרה** ולסרוק את המופעים — ראו D9.

> מנוע הדרייבר (FR-13) היה מחייב ממשקי יצרן — NVAPI (`nvapi64.dll`), AMD ADL (`atiadlxx.dll`),
> Intel IGCL. אלה **לא מתועדים באופן מלא**, ולכן הם הוגדרו מלכתחילה כיכולת נפרדת שאינה תנאי
> למוצר. ראו [DECISIONS.md](DECISIONS.md#d6).
>
> **עודכן 2026-09-14:** לפי **Q4** (אין צורך בכיסוי מסך ההתחברות ובמשחקים) **המנוע ירד
> מההיקף**, ואין מחקר מתוכנן מול ממשקי היצרנים. הארכיטקטורה אינה תלויה בו כלל.

## 8. בנייה, אריזה והפצה

* `dotnet publish -c Release -r win-x64 --self-contained` ⇒ קובץ הרצה עצמאי.
* אריזה: Inno Setup (או MSI/WiX) — התקנה למשתמש, רישום הפעלה אוטומטית, הסרה נקייה.
* CI ב-GitHub Actions על `windows-latest`: build ← test ← publish ← העלאת ארטיפקט ל-Release.
* גרסה סמנטית; כל מהדורה מתועדת ב-[CHANGELOG.md](CHANGELOG.md).
* חתימה דיגיטלית: **לא נדרשת** לפי החלטת המשתמש (Q6). בכל מהדורה יפורסם **SHA-256** של
  קובץ ההתקנה, וה-README יסביר מראש את אזהרת SmartScreen (NFR-10).

## 9. סיכונים טכניים עיקריים

| סיכון | השפעה | הפחתה |
|---|---|---|
| ~~ארבעה AppBars בו-זמנית אינם מפחיתים `rcWork` כמצופה~~ | — | **הוסר: אומת בהצלחה ב-Windows 11 את אזור העבודה מ-1920×1032 ל-1820×992.** |
| מרוץ בין ה-hook ללולאה בזמן הפעלת מסך-מלא | הבהוב קצר | לולאה מהירה יותר בהפעלה, ואז הרגעה |
| `WS_EX_TRANSPARENT` לא מספיק לחסימת קלט במצב "נעילה קשה" | קלט מגיע לשוליים | שילוב `ClipCursor` + סינון זיהוי חלונות ממוקדים |
| מסך-מלא בלעדי (DirectX) | התוכן עלול לחרוג | מתועד כמגבלה; אבחון מזהה ומדווח |
| DPI מעורב / מסך משני עם scale שונה | חישובי מיקום שגויים | Per-Monitor V2 + חישוב בקואורדינטות פיזיות בלבד |
| סשן RDP או נעילת מסך | גיאומטריה שונה זמנית | עצירה אוטומטית בזמן ניתוק/נעילה, החזרה בהתחברות |
| SmartScreen בהתקנה | חיכוך למשתמש | מתועד; אפשרות חתימה בהמשך |
