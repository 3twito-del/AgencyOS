# AgencyOS — עבודה עם Claude Code בתוך VS Code

חבילה זו מותאמת ל-Claude Code Extension ב-VS Code, אך נשארת תואמת גם ל-Claude Code CLI.

## התחלה מומלצת

1. חלץ את ה-ZIP לתיקיית פרויקט חדשה.
2. פתח **את שורש התיקייה** ב-VS Code.
3. ודא שמותקן Claude Code Extension.
4. אשר Workspace Trust רק לאחר שעברת על `.claude/settings.json` ועל `.claude/hooks/`.
5. פתח את Claude Code panel.
6. התחל ב:
   `@prompts/000_READ_FIRST.md`
7. לאחר שהחזיר הבנה תקינה, עבור ל:
   `@prompts/001_M0_REPOSITORY.md`
8. אל תבקש "Build the whole AgencyOS".

## למה יש גם `.claude/` וגם `.vscode/`

- `CLAUDE.md` = החוקה והמגבלות הקבועות של המודל.
- `.claude/settings.json` = הרשאות וה-hooks המשותפים לפרויקט.
- `.claude/skills/` = פרוצדורות חוזרות שניתן להפעיל כפקודות `/...`.
- `.vscode/tasks.json` = פקודות build/test/doctor קנוניות.
- `.vscode/extensions.json` = המלצות על extensions.
- `scripts/Invoke-AgencyOS.ps1` = entrypoint יחיד לפקודות ההנדסיות מתוך VS Code ו-Claude.

## הרשאות

הקובץ המשותף לפרויקט נותן אוטומציה רק לפעולות שגרתיות כגון:
- קריאת סטטוס/diff של Git;
- `dotnet --info`;
- סקריפט האימות המקומי.

פעולות מסוכנות נשארות Ask/Deny.

אל תגדיר `bypassPermissions` ברמת workspace. אם תרצה לשנות Permission Mode, עשה זאת בהגדרות המשתמש של Claude Code/VS Code או בכל session.

## CLI אופציונלי

התוסף כולל CLI פנימי לפאנל, אך אינו מוסיף את הפקודה `claude` ל-PATH.
אם מתקינים גם Claude Code CLI עצמאי, ניתן להשתמש בטרמינל המשולב ולחדש session עם `claude --resume`.

## Workflow

Plan/Manual בתחילת milestone.
לאחר אישור התכנית:
- Claude מבצע את ה-vertical slice;
- מפעיל `AgencyOS: Verify`;
- מעדכן roadmap;
- מוסיף ADR רק אם אכן התקבלה החלטה ארכיטקטונית.

השתמש ב-checkpoints של התוסף לפני migrations, refactors רחבים ושינויים בתצורת release.
