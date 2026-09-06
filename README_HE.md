# הערה לגרסת VS Code

אם אתה עובד עם Claude Code Extension בתוך VS Code, התחל ב-`README_VSCODE_HE.md` וב-`prompts/000A_VSCODE_SESSION_START.md`.

# AgencyOS — חבילת Bootstrap לקלוד קוד

תאריך נעילה: 2026-09-06

מטרת החבילה: להגדיר לקלוד קוד את החזון, הארכיטקטורה, מפת הדרכים, הסטנדרטים ההנדסיים, ערוצי ההפצה, מדיניות הגרסאות ומנגנון כפיית העדכונים של **AgencyOS** — מערכת הפעלה/ERP פרטית, עתידית ומקיפה לסוכנות ייצוג ובידור.

## עיקרון־על

החזון קיצוני. סדר המימוש שמרני.

- FORGE/LAB רשאים להשתמש ב־daily/HEAD/experimental ולשבור.
- NIGHTLY חייב להיבנות אוטומטית ולעבור smoke tests.
- ALPHA רשאי להכיל מידע אמיתי רק לאחר promotion gates.
- BETA/RC מיועד לבדיקת release candidate.
- STABLE הוא ערוץ ארגוני אמין.
- אין להשתמש ב־FORGE/LAB במסד הנתונים הקנוני.
- אין לבנות microservices, brokers, graph stores או vector stores רק מפני שהם קיימים.
- כל הרחבת טכנולוגיה דורשת leverage מוגדר ו־ADR.

## איך להתחיל

1. פתח את התיקייה כ־repository.
2. בקש מ-Claude Code לקרוא `CLAUDE.md`.
3. אחר כך תן לו את `prompts/000_READ_FIRST.md`.
4. המשך לפי הסדר: `001` → `002` → ... .
5. אל תדלג ל־Deals/AI לפני ש־M0–M2 עובדים end-to-end.
6. כל שינוי ארכיטקטוני מהותי חייב ADR ב־`docs/adr/`.

## היעד

AgencyOS אינו "CRM". הוא אמור להיות מערכת העצבים של הסוכנות:

People → Relationships → Talent → Projects → Opportunities → Submissions → Deals → Contracts → Rights → Finance → Documents → Intelligence → Automation → Executive Command.
