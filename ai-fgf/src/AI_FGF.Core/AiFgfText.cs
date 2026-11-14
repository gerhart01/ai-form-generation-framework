using System;
using System.Collections.Generic;
using System.Globalization;

namespace AI_FGF;

/// <summary>
/// The words the framework itself puts on screen — the log's find bar, the theme dialog, OK /
/// Cancel, the grid's filter bar, ToolForm's buttons and status — so an application in another
/// language can replace them. English by default; set once at start-up, before the first window:
/// <code>
/// AiFgfText.UseRussian();              // the built-in Russian set
/// AiFgfText.Find = "Suche:";           // or any word on its own
/// </code>
/// Texts with <c>{0}</c> are composite formats (<see cref="Format"/>). Log lines written by
/// ToolForm's run lifecycle ("[finished — exit code …]") are not covered: they are a record,
/// not chrome. A front end reads the values when it builds a control, so a change shows in
/// windows opened after it.
/// </summary>
public static class AiFgfText
{
    // ----------------------------------------------------------------- common
    public static string Ok { get; set; } = "OK";
    public static string Cancel { get; set; } = "Cancel";
    public static string Browse { get; set; } = "Browse…";
    public static string SelectFile { get; set; } = "Select file";
    public static string SaveAs { get; set; } = "Save as";
    public static string SelectFolder { get; set; } = "Select folder";
    public static string AllFilesFilter { get; set; } = "All files|*.*";
    public static string Working { get; set; } = "Working…";

    // -------------------------------------------------------------- log pane
    public static string Find { get; set; } = "Find:";
    public static string FindNext { get; set; } = "Next ▼";
    public static string FindPrevious { get; set; } = "Prev ▲";
    public static string ClearLog { get; set; } = "Clear";
    public static string NoMatches { get; set; } = "no matches";

    // ---------------------------------------------------------- theme dialog
    public static string ThemeTitle { get; set; } = "Colours";
    public static string ThemeAccent { get; set; } = "&Accent:";
    public static string ThemeCustomAccent { get; set; } = "&Custom…";
    public static string ThemeAppearance { get; set; } = "A&ppearance:";
    public static string ThemeLight { get; set; } = "Light";
    public static string ThemeDark { get; set; } = "Dark";
    public static string ThemeTint { get; set; } = "Background &tint:";
    public static string ThemeSelected { get; set; } = "selected";
    public static string ThemeButtons { get; set; } = "&Buttons:";
    public static string ButtonsFlat { get; set; } = "Flat";
    public static string ButtonsRaised { get; set; } = "Raised (3D)";
    public static string ButtonsGlow { get; set; } = "Glowing";

    /// <summary>The display name of a button style (the "Theme…" dialog).</summary>
    public static string ButtonStyleName(AiFgfButtonStyle style) => style switch
    {
        AiFgfButtonStyle.Raised => ButtonsRaised,
        AiFgfButtonStyle.Glow => ButtonsGlow,
        _ => ButtonsFlat,
    };

    /// <summary>
    /// Display names of the ready-made accents, by their English name ("Blue", "Green", …);
    /// a name missing here is shown as it is.
    /// </summary>
    public static IDictionary<string, string> AccentNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string AccentName(string name) => AccentNames.TryGetValue(name, out string? s) ? s : name;

    // ------------------------------------------------------------ data grid
    public static string Filter { get; set; } = "Filter:";
    public static string FilterHint { get; set; } = "type to filter rows across all columns…";
    public static string Export { get; set; } = "Export…";
    public static string ExportTitle { get; set; } = "Export view";
    public static string Rows { get; set; } = "{0} rows";
    public static string RowsFiltered { get; set; } = "{0} / {1} rows";
    public static string Exported { get; set; } = "Exported {0} rows";
    public static string ExportFailed { get; set; } = "Export failed: {0}";

    // -------------------------------------------------------------- ToolForm
    public static string Parameters { get; set; } = "Parameters";
    public static string Run { get; set; } = "▶ &Run";
    public static string CancelRun { get; set; } = "■ &Cancel";
    public static string SavePreset { get; set; } = "Save preset…";
    public static string LoadPreset { get; set; } = "Load preset…";
    public static string ThemeButton { get; set; } = "T&heme…";
    public static string PresetFilter { get; set; } = "Preset|*.json|All files|*.*";
    public static string Output { get; set; } = "Output";
    public static string Results { get; set; } = "Results";
    public static string Idle { get; set; } = "Idle";
    public static string Starting { get; set; } = "Starting…";
    public static string Running { get; set; } = "Running…";
    public static string Done { get; set; } = "Done";
    public static string DoneWithWarnings { get; set; } = "Finished with warnings";
    public static string DoneWithErrors { get; set; } = "Finished with errors";
    public static string TimedOut { get; set; } = "Timed out";
    public static string Cancelled { get; set; } = "Cancelled";
    public static string Error { get; set; } = "Error";
    public static string FieldsNeedAttention { get; set; } = "{0} fields need attention";

    // ------------------------------------------------------------ crash box
    public static string CrashTitle { get; set; } = "Unexpected error";
    public static string CrashMessage { get; set; } = "An unexpected error occurred:\n\n{0}\n\n{1}\n\nThe application will keep running.";
    public static string CrashReportSaved { get; set; } = "A report was saved to:\n{0}";
    public static string CrashReportFailed { get; set; } = "(the crash report could not be written)";

    /// <summary><see cref="string.Format(IFormatProvider, string, object[])"/> in the current culture.</summary>
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    // The initialisers above are the English set; kept here so UseEnglish can put them back.
    private static readonly (System.Reflection.PropertyInfo Property, object? Value)[] English =
        Array.ConvertAll(Array.FindAll(typeof(AiFgfText).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
                                       p => p.PropertyType == typeof(string) && p.CanWrite),
                         p => (p, p.GetValue(null)));

    /// <summary>Back to the English defaults (accent names included).</summary>
    public static void UseEnglish()
    {
        foreach (var (p, v) in English) p.SetValue(null, v);
        AccentNames.Clear();
    }

    /// <summary>The built-in Russian set.</summary>
    public static void UseRussian()
    {
        Ok = "ОК"; Cancel = "Отмена"; Browse = "Обзор…"; SelectFile = "Выбор файла"; SaveAs = "Сохранить как";
        SelectFolder = "Выбор папки"; AllFilesFilter = "Все файлы|*.*"; Working = "Выполняется…";
        Find = "Найти:"; FindNext = "Далее ▼"; FindPrevious = "Назад ▲"; ClearLog = "Очистить"; NoMatches = "не найдено";
        ThemeTitle = "Цвета"; ThemeAccent = "&Акцент:"; ThemeCustomAccent = "&Другой…"; ThemeAppearance = "&Оформление:";
        ThemeLight = "Светлое"; ThemeDark = "Тёмное"; ThemeTint = "&Оттенок фона:"; ThemeSelected = "выбран";
        ThemeButtons = "&Кнопки:"; ButtonsFlat = "Плоские"; ButtonsRaised = "Объёмные (3D)"; ButtonsGlow = "Светящиеся";
        AccentNames.Clear();
        foreach (var (en, ru) in new[]
        {
            ("Blue", "Синий"), ("Indigo", "Индиго"), ("Purple", "Фиолетовый"), ("Pink", "Розовый"), ("Red", "Красный"),
            ("Orange", "Оранжевый"), ("Gold", "Золотой"), ("Green", "Зелёный"), ("Teal", "Бирюзовый"), ("Graphite", "Графит"),
        }) AccentNames[en] = ru;
        Filter = "Фильтр:"; FilterHint = "строки, где есть этот текст в любом столбце…"; Export = "Экспорт…"; ExportTitle = "Экспорт таблицы";
        Rows = "строк: {0}"; RowsFiltered = "строк: {0} из {1}"; Exported = "выгружено строк: {0}"; ExportFailed = "не удалось выгрузить: {0}";
        Parameters = "Параметры"; Run = "▶ &Запустить"; CancelRun = "■ &Прервать"; SavePreset = "Сохранить набор…"; LoadPreset = "Загрузить набор…";
        ThemeButton = "&Тема…"; PresetFilter = "Набор|*.json|Все файлы|*.*"; Output = "Вывод"; Results = "Результаты";
        Idle = "Ожидание"; Starting = "Запуск…"; Running = "Выполняется…"; Done = "Готово"; DoneWithWarnings = "Готово, есть предупреждения";
        DoneWithErrors = "Завершено с ошибками"; TimedOut = "Превышено время"; Cancelled = "Прервано"; Error = "Ошибка";
        FieldsNeedAttention = "полей с ошибками: {0}";
        CrashTitle = "Непредвиденная ошибка";
        CrashMessage = "Произошла непредвиденная ошибка:\n\n{0}\n\n{1}\n\nПриложение продолжит работу.";
        CrashReportSaved = "Отчёт сохранён:\n{0}"; CrashReportFailed = "(отчёт об ошибке записать не удалось)";
    }
}
