using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CanonServiceDesk {
    public static class AppInfo { public const string Version="0.2.0"; }
    public static class Json {
        public static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength = 16000000 }.Serialize(value); }
        public static T Decode<T>(string value) { return new JavaScriptSerializer { MaxJsonLength = 16000000 }.Deserialize<T>(value); }
    }
    public sealed class Journal {
        public readonly string Folder;
        readonly object sync = new object();
        public Journal(string folder) { Folder = folder; Directory.CreateDirectory(folder); }
        public void Add(string level, string step, string message, object data = null) {
            string time = DateTimeOffset.Now.ToString("o");
            lock(sync) {
                File.AppendAllText(Path.Combine(Folder,"events.jsonl"), Json.Encode(new { time, level, step, message, data }) + "\n", Encoding.UTF8);
                File.AppendAllText(Path.Combine(Folder,"journal.txt"), time + " [" + level + "] " + step + " — " + message + "\r\n" + (data == null ? "" : Json.Encode(data) + "\r\n"), Encoding.UTF8);
            }
        }
        public void Save(Snapshot state) {
            File.WriteAllText(Path.Combine(Folder,"report.json"), Json.Encode(state), Encoding.UTF8);
        }
    }
    public sealed class Device {
        public string Name = "", InstanceId = "", HardwareIds = "", Manufacturer = "", Service = "", Class = "";
        public uint ProblemCode;
        public bool ProblemCodeKnown;
    }
    public sealed class UsbResult {
        public string Name = "", InstanceId = "", Path = "", DeviceId = "", RawHex = "";
        public string Model = "", ErrorStage = "";
        public bool ReadOk;
        public int Win32Error;
    }
    public sealed class Snapshot {
        public string Version = AppInfo.Version, CreatedUtc = DateTime.UtcNow.ToString("o"), OS = Environment.OSVersion.ToString();
        public bool Completed, Demo;
        public List<Device> Devices = new List<Device>();
        public List<UsbResult> Usb = new List<UsbResult>();
        public List<string> Printers = new List<string>();
        public List<string> Errors = new List<string>();
    }
    public sealed class Finding {
        public string Title, Evidence, Action;
        public Finding(string title, string evidence, string action) { Title=title; Evidence=evidence; Action=action; }
        public override string ToString() { return Title + "\r\n" + Evidence + "\r\nЧто сделать: " + Action; }
    }
    public static class Rules {
        public static string Win32(int n) {
            switch(n) {
                case 0: return "Операция выполнена.";
                case 2: case 3: return "Путь устройства исчез. Обновите список после подключения USB или смены режима.";
                case 5: return "Windows отказала в доступе. Закройте другие программы принтера; если отказ остаётся, попробуйте запуск от администратора.";
                case 6: return "Недействительный дескриптор Windows. Возможно, устройство переподключилось во время запроса. Это НЕ код 006 Service Tool.";
                case 23: return "Ошибка CRC при USB-запросе. Возможны размер буфера, драйвер или связь; один этот код не доказывает повреждение принтера.";
                case 31: return "Драйвер не выполнил запрос. Проверьте его состояние и USB; нужна запись обмена для уточнения.";
                case 32: return "Устройство занято другим процессом. Закройте Service Tool, PrintHelp и окно состояния Canon, затем повторите проверку.";
                case 1: case 50: return "Драйвер не поддержал запрос. Проверьте, что выбран интерфейс USBPRINT и установлен подходящий драйвер.";
                case 121: case 1460: return "Истекло время ожидания. Проверьте кабель и прямое подключение USB; затем повторите один раз.";
                case 1167: return "Устройство отключено. Подключите его и обновите список.";
                default: return new Win32Exception(n).Message + " (код Windows " + n + ").";
            }
        }
        public static string ServiceTool(string input) {
            int n;
            if(!int.TryParse(input.Trim(), out n)) return "Введите числовой код Service Tool, например 006.";
            switch(n) {
                case 2: return "002: утилита не поддержала операцию/модель. Это не доказательство повреждения EEPROM. Сравните определение модели и запишите обмен.";
                case 5: return "005: Service Tool не распознаёт или не открывает принтер. Посмотрите USB-ответ и ошибки открытия. Если PrintHelp видит его, проверьте совместимость Service Tool и занятость устройства.";
                case 6: return "006: Service Tool не смог работать с принтером как с сервисным устройством. Частая причина — обычный режим, но если PrintHelp в том же состоянии видит сервисный режим, код не доказывает неправильный вход или поломку. Нужна запись обмена.";
                case 9: return "009: Service Tool сообщает о дополнительной ошибке принтера. Нужен код с панели или сохранённая информация EEPROM.";
                default: return "Для кода " + input + " нет проверенной расшифровки в этой версии. Сохраните журнал и точный текст окна.";
            }
        }
        public static List<Finding> Diagnose(Snapshot s) {
            var f = new List<Finding>();
            if(s.Demo) f.Add(new Finding("Демонстрационные данные", "Принтер не опрашивался.", "Запустите обычный режим на Windows для проверки устройства."));
            if(!s.Completed) f.Add(new Finding("Проверка не завершена", "Отчёт содержит только доступную часть данных.", "Посмотрите последнюю операцию в журнале; не делайте вывод об отсутствии устройства по частичному отчёту."));
            foreach(var d in s.Devices.Where(x=>x.ProblemCodeKnown && x.ProblemCode!=0)) {
                string action = d.ProblemCode==28 ? "Установите официальный MP Driver для G2411 в обычном режиме. Отсутствующий драйвер одного интерфейса не означает, что USB в сервисном режиме тоже не работает." : "Откройте свойства этого устройства в диспетчере устройств и проверьте подробный код Windows.";
                f.Add(new Finding("Проблема драйвера: " + d.Name, "Код диспетчера устройств: " + d.ProblemCode + "; служба: " + d.Service, action));
            }
            foreach(var u in s.Usb) {
                if(u.ReadOk) f.Add(new Finding("USB отвечает: " + (u.Model.Length>0 ? u.Model:u.Name), "Получена строка IEEE 1284, " + u.RawHex.Replace(" ","").Length/2 + " байт.", "Базовая связь работает. Сервисный режим и поддержка сброса этим чтением не подтверждаются. Для 005/006 запишите обмен самой Service Tool."));
                else f.Add(new Finding("Не удалось прочитать USB: " + u.Name, u.ErrorStage + "; Win32=" + u.Win32Error,
                    u.Win32Error==0 ? "Windows приняла запрос, но полноценный идентификатор не получен. Нужен разбор сохранённых байтов ответа; успех сброса этим не подтверждается.":Win32(u.Win32Error)));
            }
            if(s.Completed && s.Devices.Count==0 && s.Usb.Count==0) f.Add(new Finding("Canon не найден среди подключённых устройств", "Поиск выполнен среди присутствующих PnP-устройств и USBPRINT-интерфейсов.", "Проверьте питание и прямое подключение USB, затем обновите список. Сетевые принтеры сюда не входят."));
            else if(s.Completed && s.Usb.Count==0) f.Add(new Finding("USBPRINT-интерфейс Canon не найден", "Устройства Canon есть, но открыть стандартный USB-интерфейс печати нечего.", "Сверьте службу драйвера в списке. Если установлен WinUSB/libusb, не меняйте его наугад: приложите отчёт. Проверьте режим и подключение."));
            if(s.Completed && s.Printers.Count==0) f.Add(new Finding("Очередь печати Canon не найдена", "Пробная страница и окно драйвера пока недоступны.", "Для обычной печати установите официальный драйвер и включите обычный режим. Для Service Tool наличие очереди не обязательно."));
            if(s.Errors.Count>0) f.Add(new Finding("Есть ошибки сбора данных", string.Join("; ",s.Errors), "Приложите журнал. Частичная ошибка сбора не равна неисправности принтера."));
            return f;
        }
    }
    public static class DeviceIdParser {
        public static string Decode(byte[] data) {
            if(data==null || data.Length==0) return "";
            int start=0, count=data.Length;
            // usbprint returns a two-byte size followed by the ASCII device ID.
            // Some implementations omit the prefix. Detect a leading ASCII key.
            bool plain = data.Length>=4 && ((data[0]>=65 && data[0]<=90) || (data[0]>=97 && data[0]<=122)) && Array.IndexOf(data,(byte)':',0,Math.Min(16,data.Length))>=0;
            if(!plain && data.Length>=2) {
                start=2; count=data.Length-2;
                int declared=(data[0]<<8)|data[1];
                if(declared>=2 && declared<=data.Length) count=declared-2;
            }
            string result=Encoding.ASCII.GetString(data,start,count);
            int nul=result.IndexOf('\0');
            return (nul<0 ? result:result.Substring(0,nul)).Trim();
        }
        public static string Field(string id,string key) {
            foreach(string part in id.Split(';')) {
                int colon=part.IndexOf(':');
                if(colon>0 && part.Substring(0,colon).Trim().Equals(key,StringComparison.OrdinalIgnoreCase)) return part.Substring(colon+1).Trim();
            }
            return "";
        }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace('-',' '); }
    }
    public static class WindowsArgs {
        public static string Quote(string arg) {
            var b=new StringBuilder("\""); int slashes=0;
            foreach(char c in arg) {
                if(c=='\\') { slashes++; continue; }
                if(c=='"') b.Append('\\',slashes*2+1).Append(c);
                else b.Append('\\',slashes).Append(c);
                slashes=0;
            }
            return b.Append('\\',slashes*2).Append('"').ToString();
        }
    }
}
