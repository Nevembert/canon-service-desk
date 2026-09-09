using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CanonServiceDesk {
    public sealed class ServiceProcedure {
        public string Id, Title, Method, Description, Source;
        public string[] Steps;
        public override string ToString() { return Title; }
    }
    public static class ServiceCatalog {
        const string Manual="https://ij.manual.canon/ij/webmanual/Manual/All/G2010%20series/EN/UG/";
        const string Driver="https://ij.manual.canon/ij/webmanual/PrinterDriver/W/G2010%20series/1.0/EN/PPG/";
        static ServiceProcedure Item(string id,string title,string method,string description,string source,params string[] steps) {
            return new ServiceProcedure { Id=id,Title=title,Method=method,Description=description,Source=source,Steps=steps };
        }
        public static readonly ServiceProcedure[] All = {
            Item("absorber","Абсорбер: сброс P07 / 5B00","Кнопки принтера · сообщение владельца G2411",
                "Мастер проводит через способ, который помог владельцу G2411. Запись в EEPROM через USB не выполняется. Для другой модели последовательность не подтверждена.","https://github.com/Nevembert/canon-service-desk#сброс-абсорбера",
                "Проверьте модель на корпусе: G2411. Абсорбер должен быть обслужен и установлен. Запишите исходный код ошибки и показания счётчиков, если они доступны.",
                "Войдите в сервисный режим известным вам способом для G2411. Этот мастер начинается ПОСЛЕ входа: обычное включение не заменяет вход в сервисный режим.",
                "На панели принтера нажмите «Отмена / Stop» 5 раз, затем «Включение / Power» 1 раз. Это последовательность из сообщения владельца G2411.",
                "Дождитесь окончания движений. Если принтер остался в сервисном режиме, выключите его кнопкой Power, затем включите обычным способом. Не отключайте питание во время движений.",
                "Проверьте, исчезла ли P07 / 5B00, и выполните тест дюз. Выберите фактический результат ниже. Сообщение пользователя не является автоматическим чтением счётчика."),
            Item("nozzle","Тест дюз — режим 1","Панель принтера / Canon IJ","Проверяет чёрный и цветные каналы печати.",Manual+"ug_m_01_02_c.html",
                "Обычный режим. Проверьте чернила, положите один лист A4 в задний лоток и выдвиньте выходной лоток.",
                "Нажмите Setup: на дисплее появится 1. Нажмите Black или Color и дождитесь листа.",
                "Проверьте чёрную сетку и цветные полосы. Пропуски или белые полосы — повод для очистки. Сохраните результат в журнале."),
            Item("clean","Обычная очистка — режим 2","Панель принтера / Canon IJ","Расходует чернила. Выполняйте при пропусках в тесте дюз.",Manual+"ug_m_01_04_c.html",
                "Обычный режим, чернил достаточно. Нажмите Setup, кнопкой + выберите 2.",
                "Нажмите Black или Color. Дождитесь постоянного индикатора питания: обычно около минуты.",
                "Повторите тест дюз. Если две обычные очистки не помогли, следующая процедура — глубокая очистка."),
            Item("deep","Глубокая очистка — режим 3","Панель принтера / Canon IJ","Расходует больше чернил; применяется после двух безуспешных обычных очисток.",Manual+"ug_m_01_05_c.html",
                "Проверьте запас чернил. В обычном режиме нажмите Setup и выберите 3 кнопкой +.",
                "Нажмите Black или Color. Не запускайте другие операции; дождитесь постоянного индикатора питания (около трёх минут).",
                "Выполните тест дюз и сравните его с исходным листом."),
            Item("align","Выравнивание головки — режим 5","Панель принтера / Canon IJ","Печатает и сканирует лист для коррекции положения головки.",Manual+"ug_m_01_07_c.html",
                "Проверьте чернила. Положите чистый с обеих сторон лист A4 в задний лоток, откройте выходной лоток.",
                "Setup → + до 5 → Black или Color. Дождитесь листа выравнивания; не пачкайте печатную сторону.",
                "Положите лист печатью вниз на стекло, совместив его отметку с отметкой на стекле, как на схеме в инструкции Canon.",
                "Закройте крышку, нажмите Black или Color. Дождитесь завершения сканирования и постоянного индикатора. Уберите лист."),
            Item("alignment-report","Печать значений выравнивания — 7","Панель принтера","Печатает текущие значения коррекции головки.",Manual+"ug_m_01_07_c.html",
                "В обычном режиме загрузите лист A4 и откройте выходной лоток.",
                "Setup → + до 7 → Black или Color. Дождитесь выхода листа."),
            Item("rollers","Очистка роликов подачи — режим 8","Панель принтера / Canon IJ","Два этапа: вращение без бумаги и затем прогон трёх листов.",Manual+"ug_m_04_04_c.html",
                "Принтер включён в обычном режиме. Уберите бумагу из заднего лотка.",
                "Setup → + до 8 → Black или Color. Дождитесь остановки роликов.",
                "Загрузите три листа A4, выдвиньте выходной лоток. Ещё раз нажмите Black или Color.",
                "Дождитесь выхода бумаги, затем нажмите Stop. Проверьте, восстановилась ли подача."),
            Item("bottom","Очистка поддона — режим 9","Панель принтера / Canon IJ","Помогает при следах чернил с обратной стороны бумаги.",Manual+"ug_m_04_05_c.html",
                "Возьмите новый лист A4. Сложите пополам поперёк, затем разверните.",
                "Положите только этот лист в задний лоток раскрытой стороной к себе (схема — по ссылке Canon). Выдвиньте выходной лоток.",
                "Setup → + до 9 → Black или Color. Осмотрите сгибы вышедшего листа. При повторе используйте новый лист."),
            Item("ink-level","Сброс учёта чернил после заправки","Через Canon IJ Printer Assistant Tool","Сбрасывает расчёт остатка чернил. Счётчики абсорбера и общего пробега этим не сбрасываются.",Driver+"Dg-printer_assistant.html",
                "Заправьте ВСЕ ёмкости до верхней отметки. Без этого расчёт остатка после сброса будет неверным.",
                "Нажмите «Открыть Canon IJ / драйвер». На вкладке «Обслуживание» выберите «Обслуживание и настройки», чтобы открыть Canon IJ Printer Assistant Tool.",
                "Откройте Remaining Ink Notification Settings / настройки уведомления об остатке. В разделе Resets the Remaining Ink Level Count нажмите Reset и выполните указания Canon."),
            Item("flush","Прокачка чернил — Ink Flush","Через Canon IJ Printer Assistant Tool","Сильно расходует чернила и заполняет абсорбер; применяется, если глубокая очистка не помогла.",Driver+"Dg-printer_assistant.html",
                "Убедитесь, что абсорбер обслужен при необходимости. При выборе All Colors или Black чернила во ВСЕХ баках должны быть не ниже точки на шкале. Для Color — во всех цветных баках.",
                "Откройте Canon IJ Printer Assistant Tool через вкладку обслуживания драйвера. Выберите Ink Flush и нужную группу чернил.",
                "Проверьте условия в окне Canon и запустите одну прокачку. Дождитесь её завершения, затем выполните тест дюз."),
            Item("page-reset","Обнуление общего пробега","USB-команда для G2411 не подтверждена","В этой версии аппаратный Total / TPAGE не обнуляется. Можно сохранить его показания и начать локальный отсчёт страниц после обслуживания на вкладке «Счётчики».","https://github.com/Nevembert/canon-service-desk/blob/main/docs/SERVICE-SUPPORT.md",
                "Сохраните сервисный отчёт или перепишите Total, Print и Blank с распечатки. При импорте программа показывает источник значений.",
                "Для отсчёта после ремонта используйте «Начать отсчёт после обслуживания». Это локальная отметка приложения; значение в памяти принтера остаётся прежним.",
                "Для реализации аппаратного сброса нужен подтверждённый обмен именно этой модели и показания до/после. Наблюдатель Service Tool из папки trace сохраняет такой обмен без его изменения.")
        };
    }
    public static class BjlCommands {
        public static bool Advertised(string deviceId) {
            string mfg=DeviceIdParser.Field(deviceId,"MFG");
            if(mfg.Length==0) mfg=DeviceIdParser.Field(deviceId,"MANUFACTURER");
            if(!mfg.Equals("Canon",StringComparison.OrdinalIgnoreCase)) return false;
            string cmd=DeviceIdParser.Field(deviceId,"CMD");
            if(cmd.Length==0) cmd=DeviceIdParser.Field(deviceId,"COMMAND SET");
            return cmd.Split(',').Any(x=>x.Trim().Equals("BJL",StringComparison.OrdinalIgnoreCase));
        }
        public static byte[] Build(string operation) {
            string command;
            switch(operation) {
                case "nozzle": command="@TestPrint=NozzleCheck";break;
                case "clean-all": command="@Cleaning=1ALL";break;
                case "clean-black": command="@Cleaning=1K";break;
                default: throw new InvalidOperationException("Нет подтверждённой BJL-команды для этой операции.");
            }
            return new byte[]{0x1b,0x5b,0x4b,2,0,0,0x1f}.Concat(Encoding.ASCII.GetBytes("BJLSTART\n"+command+"\nBJLEND\n")).ToArray();
        }
    }
    public sealed class CounterReading {
        public long? Total, Printed, Blank;
        public string Raw="", CreatedUtc=DateTime.UtcNow.ToString("o"), Source="operator_text";
    }
    public static class CounterParser {
        static long? Field(string text,string pattern) {
            var matches=Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Multiline);
            long? found=null;
            foreach(Match m in matches) {
                long n;
                if(!long.TryParse(m.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out n)) throw new FormatException("Счётчик не помещается в допустимый числовой диапазон.");
                if(found.HasValue && found.Value!=n) throw new FormatException("В тексте несколько разных значений одного счётчика. Импортируйте один отчёт.");
                found=n;
            }
            return found;
        }
        public static CounterReading Parse(string text) {
            if(text==null || text.Length>65536) throw new FormatException("Текст отчёта должен быть не больше 64 КБ.");
            const string end=@"(?:\s+Pages)?[ \t]*\r?$";
            var r=new CounterReading { Raw=text,
                Total=Field(text,@"^[ \t]*Total(?:[ \t]*[:=][ \t]*|[ \t]+)([0-9]+)"+end),
                Printed=Field(text,@"^[ \t]*Print(?:[ \t]*[:=][ \t]*|[ \t]+)([0-9]+)"+end),
                Blank=Field(text,@"^[ \t]*Blank(?:[ \t]*[:=][ \t]*|[ \t]+)([0-9]+)"+end) };
            long? tpage=Field(text,@"(?:^|[;\s])TPAGE[ \t]*=[ \t]*([0-9]+)(?=[;\s]|$)");
            if(r.Total.HasValue && tpage.HasValue && r.Total!=tpage) throw new FormatException("Total и TPAGE различаются. Уточните исходный отчёт.");
            if(!r.Total.HasValue) r.Total=tpage;
            if(!r.Total.HasValue && !r.Printed.HasValue && !r.Blank.HasValue) throw new FormatException("Числа не распознаны. Используйте строки Total 7522 Pages, Print 7469 Pages, Blank 53 Pages или TPAGE=7522. Фото и двоичный EEPROM этим полем не читаются.");
            if(r.Total.HasValue && r.Printed.HasValue && r.Blank.HasValue && (r.Printed>r.Total || r.Blank!=r.Total-r.Printed)) throw new FormatException("Total не совпадает с Print + Blank. Проверьте числа на исходном листе.");
            return r;
        }
        public static string Display(long? value) { return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture):"не указан"; }
        public static string Delta(CounterReading before,CounterReading after) {
            if(before==null || after==null || !before.Total.HasValue || !after.Total.HasValue) return "Для сравнения нужны оба значения Total.";
            if(after.Total<before.Total) return "Total уменьшился. Проверьте устройство и источник данных; автоматический сброс этим не подтверждён.";
            return "Разница Total: "+(after.Total.Value-before.Total.Value)+". Значения внесены пользователем.";
        }
    }
    public sealed class ServiceEntry {
        public string CreatedUtc=DateTime.UtcNow.ToString("o"), Device="", Kind="", Operation="", Outcome="", Note="", Source="user_report";
        public CounterReading Counters;
    }
    public sealed class ServiceHistory {
        readonly string root;
        public ServiceHistory(string root) { this.root=root; }
        string PathFor(string device) {
            if(string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Введите имя или серийный номер устройства для истории.");
            using(var sha=SHA256.Create()) return Path.Combine(root,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(device.Trim()))).Replace("-","")+".jsonl");
        }
        public void Append(ServiceEntry entry) {
            string path=PathFor(entry.Device);Directory.CreateDirectory(root);
            File.AppendAllText(path,Json.Encode(entry)+"\n",Encoding.UTF8);
        }
        public List<ServiceEntry> Read(string device) {
            string path=PathFor(device);var rows=new List<ServiceEntry>();
            if(!File.Exists(path)) return rows;
            foreach(string line in File.ReadLines(path)) {
                if(string.IsNullOrWhiteSpace(line)) continue;
                var row=Json.Decode<ServiceEntry>(line);
                if(row==null || row.Device.Trim()!=device.Trim()) throw new FormatException("История повреждена или относится к другому устройству.");
                rows.Add(row);
            }
            return rows;
        }
    }
    public sealed class QueueJob {
        public uint Id, Status, TotalPages, PagesPrinted;
        public string Document="", Owner="", Submitted="", State="";
    }
    public sealed class QueueReport {
        public string Printer="", Error="";
        public List<QueueJob> Jobs=new List<QueueJob>();
        public bool Complete;
    }
    public sealed class ServiceRequest {
        public string Operation="", Printer="", UsbPath="", DeviceId="";
        public QueueJob Job;
    }
}
