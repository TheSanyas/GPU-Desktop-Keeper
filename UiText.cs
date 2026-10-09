using System;
using System.Collections.Generic;
using System.Globalization;

namespace GpuDesktopKeeper {
    // Stable Russian source keys; diagnostic details from Windows/drivers remain verbatim.
    internal static class UiText {
        internal static string Language="ru";
        internal static string Normalize(string value) { return String.Equals(value,"en",StringComparison.OrdinalIgnoreCase) ? "en" : "ru"; }
        private static readonly Dictionary<string,string> English=new Dictionary<string,string> {
            {"Фикс зависаний приложений на дополнительных мониторах","Fix for app stutter on secondary monitors"},
            {"Запуск…","Starting…"},
            {"Главная","Home"}, {"Эксперименты","Experiments"},
            {"Выключить","Disable"}, {"Включить","Enable"},
            {"Перезапустить фикс","Restart fix"},
            {"Поведение программы","Application behavior"},
            {"Восстанавливать фикс после сна и при потере устройства","Restore the fix after sleep or device loss"},
            {"Проверять доступность GPU каждые 10 секунд","Check GPU availability every 10 seconds"},
            {"При запуске сразу сворачивать окно в трей","Start minimized to the system tray"},
            {"Включать фикс при запуске программы","Enable the fix when the application starts"},
            {"Запоминать успешно применённый режим","Remember the last successfully applied mode"},
            {"Автозапуск при запуске системы","Start with Windows"},
            {"Статус «включён» означает, что программа удерживает свои GPU-ресурсы. Плавность оценивается в игре.","“Enabled” means the application is holding its GPU resources. Check smoothness in-game."},
            {"Сравнение режимов","Compare modes"},
            {"Режим применяется сразу после нажатия кнопки. Затем верни фокус в игру и наблюдай тот же видеоряд на втором мониторе минимум 30 секунд.","The mode is applied immediately. Return focus to the game and watch the same video on your second monitor for at least 30 seconds."},
            {"Применить","Apply"}, {"Вернуть основной режим","Restore default mode"},
            {"Отменить восстановление","Cancel recovery"},
            {"Все изменения относятся только к работе этой программы.","These settings only affect this application."},
            {"Включить светлую тему","Switch to light theme"}, {"Включить тёмную тему","Switch to dark theme"},
            {"Переключить на английский","Switch to English"}, {"Переключить на русский","Switch to Russian"},
            {"Эксперимент — устройство и два буфера","Experimental — device and two buffers"},
            {"Основной — только устройство 11.1","Default — device 11.1 only"},
            {"Эксперимент — устройство и буфер 96 байт","Experimental — device and a 96-byte buffer"},
            {"Эксперимент — устройство и буфер 8192 байта","Experimental — device and an 8192-byte buffer"},
            {"Фикс включён","Fix enabled"}, {"Фикс выключен","Fix disabled"}, {"Не удалось включить","Could not enable the fix"},
            {"Восстановление: попытка {0} из 3","Recovery: attempt {0} of 3"},
            {"Сейчас: {0}","Current mode: {0}"},
            {"Устройство 11.1 · буферов: {0} · данные буферов: {1} байт","Device 11.1 · buffers: {0} · buffer data: {1} bytes"},
            {"GPU-ресурсы освобождены","GPU resources released"},
            {"Инициализаций в этом запуске: {0}","Initializations this session: {0}"},
            {"Не удалось прочитать автозапуск: {0}","Could not read startup settings: {0}"},
            {"Автозапуск указывает на другую копию Keeper. Сними и снова включи галочку, чтобы выбрать эту копию.","Startup points to another copy of Keeper. Uncheck and recheck the option to use this copy."},
            {"Есть прежняя запись Keeper в реестре. Включи галочку, чтобы перенести автозапуск в планировщик.","An older Keeper startup entry exists in the registry. Check the option to migrate it to Task Scheduler."},
            {"GPU Desktop Keeper уже запущен.","GPU Desktop Keeper is already running."},
            {"Не удалось продолжить работу. Собственные ресурсы освобождены.\n","The application could not continue. Its resources have been released.\n"},
            {"Открыть","Open"}, {"Выход","Exit"},
            {"Успешно применённый режим сохранён для следующего запуска.","The successfully applied mode has been saved for the next launch."},
            {"Режим применён. При следующем запуске будет выбран основной режим.","Mode applied. The default mode will be used on the next launch."},
            {"Не удалось сохранить настройки: {0}","Could not save settings: {0}"},
            {"Настройки программы сохранены.","Application settings saved."},
            {"Задача создана: при входе любого пользователя, выполнение в твоём сеансе.","Task created: any user logon triggers it; it runs in your session."},
            {"Задача автозапуска удалена.","Startup task removed."},
            {"Не удалось изменить автозапуск: {0}","Could not change startup settings: {0}"},
            {"включено","enabled"}, {"выключено","disabled"}, {"ошибка","error"},
            {"Flydigi может мешать работе фикса","Flydigi may interfere with the fix"},
            {"Flydigi Space Station может негативно влиять на этот фикс. Если лаги возвращаются, полностью закрой Flydigi через его значок в трее.","Flydigi Space Station may interfere with this fix. If stutter returns, completely exit Flydigi using its system tray icon."},
            {"Скрывать при последующих запусках","Do not show again"}, {"Понятно","Got it"},
            {"GPU недоступен: 0x","GPU unavailable: 0x"},
            {"Запись автозапуска имеет неизвестный формат.","The startup entry has an unknown format."},
            {"Для автозапуска нужен полный путь к EXE.","An absolute EXE path is required for startup."},
            {"EXE не найден. Автозапуск не изменён.","EXE not found. Startup settings were not changed."},
            {"Планировщик не подтвердил создание задачи.","Task Scheduler did not confirm task creation."},
            {"Не удалось удалить задачу автозапуска.","Could not remove the startup task."},
            {"Не удалось удалить прежнюю запись Keeper из реестра.","Could not remove the previous Keeper registry entry."},
            {" Ошибка возврата прежнего автозапуска: "," Could not restore the previous startup settings: "},
            {"Не удалось изменить задачу. Причина показана в окне настройки автозапуска.","Could not change the task. Details are shown in the startup settings dialog."},
            {"Изменение автозапуска отменено в запросе Windows.","The startup change was cancelled in the Windows prompt."},
            {"Подтверди запрос Windows той же учётной записью, в которой запущен Keeper.","Approve the Windows prompt using the same account that is running Keeper."},
            {"Не удалось изменить автозапуск: ","Could not change startup settings: "},
            {"GPU Desktop Keeper — автозапуск","GPU Desktop Keeper — startup"},
            {"Некорректный путь автозапуска.","Invalid startup path."},
            {"GPU Desktop Keeper: запуск в трей при входе любого пользователя; выполнение в интерактивном сеансе владельца задачи.","GPU Desktop Keeper: start in the tray at any user logon; run in the task owner's interactive session."},
            {"Задача с таким именем не принадлежит этой установке Keeper. Она не изменена.","A task with this name does not belong to this Keeper installation. It was not changed."},
            {"Неизвестное действие в задаче Keeper.","Unknown action in the Keeper task."},
            {"У задачи Keeper отсутствует путь к EXE.","The Keeper task is missing its EXE path."},
            {"проверка интерфейса","UI preview"}
        };
        internal static string Get(string key) {
            string value;
            return Language=="en" && English.TryGetValue(key,out value) ? value : key;
        }
        internal static string Format(string key,params object[] args) { return String.Format(CultureInfo.InvariantCulture,Get(key),args); }
        internal static string Status(KeeperEngine engine,bool recovering,int attempt) {
            return recovering ? Format("Восстановление: попытка {0} из 3",attempt) : Get(engine.Active ? "Фикс включён" : engine.DesiredEnabled ? "Не удалось включить" : "Фикс выключен");
        }
    }
}
