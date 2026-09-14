# FrameFit — הודעות צד-שלישי

קובץ זה מפרט את רכיבי הצד-שלישי הנכללים בהפצה של FrameFit, ואת הרישיונות החלים עליהם.
הוא קיים משום שרישיונות אלו מחייבים **לשמר את הודעת הזכויות שלהם** כאשר התוצר מופץ.

## למה זה נדרש

FrameFit מופץ בשני אופנים — קובץ הרצה ניידי ו-MSI — ושניהם ארוזים כ-**self-contained**,
כלומר מנוע ההרצה של .NET כלול **בתוך** הקובץ ולא נדרש להתקין אותו בנפרד. מכאן שההפצה
של FrameFit היא גם הפצה של רכיבי .NET, והרישיון שלהם (MIT) מחייב לצרף את הודעתם.

## מה נכלל בהפצה

| רכיב | היכן הוא נוכח | רישיון |
|---|---|---|
| .NET 10 Runtime | מנוע ההרצה, ארוז בתוך `FrameFit.exe` | MIT |
| .NET Windows Desktop Runtime — WPF | ספריות התצוגה, ארוזות בתוך `FrameFit.exe` | MIT |
| .NET Windows Desktop Runtime — Windows Forms | ספריות עזר לתצוגה, ארוזות בתוך `FrameFit.exe` | MIT |
| WiX Toolset v5 — `WixToolset.UI.wixext` | ספריית ממשק אשף ההתקנה, מוטבעת בתוך ה-MSI | MS-RL |
| FrameFit עצמו | — | MIT — ראו [`LICENSE`](LICENSE) |

**אין** תלות ב-NuGet של צד-שלישי בקוד האפליקציה. חבילות הבדיקה (`xunit`,
`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) משמשות לבדיקות בלבד, אינן נכללות
בהפצה, ולכן אינן מופיעות כאן.

---

## רכיבי .NET 10 — רישיון MIT

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

מקור: <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

---

## WiX Toolset v5 — רישיון MS-RL

הרישיון הוא **Microsoft Reciprocal License (MS-RL)**. זהו רישיון הדדי (weak copyleft)
החל על **קוד המקור של WiX עצמו**:

* הוא **אינו** מחייב לפתוח את קוד המקור של המתקין שנבנה בעזרת WiX, ואינו מגביל הפצה
  מסחרית של התוצר הבנוי.
* הוא **כן** מחייב לשמר את הודעת הזכויות של WiX בהפצה של קבצים בינאריים של WiX —
  ובמקרה שלנו, ספריית `WixToolset.UI.wixext` המוטבעת בתוך ה-MSI.

טקסט הרישיון המלא: <https://github.com/wixtoolset/wix/blob/main/LICENSE.TXT>

> **מצב נוכחי (2026-09-14):** הקובץ הזה קיים במאגר, אך **טרם נכלל בתוך ה-MSI**.
> כלומר חובת ההפצה של הודעת WiX עדיין אינה ממולאת למי שמתקין רק את קובץ ה-MSI.
> המשימה פתוחה ומסומנת ב-[`docs/TASKS.md`](docs/TASKS.md).

---

## עדכון הקובץ

יש לעדכן קובץ זה כאשר מתווסף רכיב צד-שלישי חדש להפצה — למשל אם תתווסף תלות NuGet
לאפליקציה, או אם יוחלף WiX בכלי אריזה אחר.
