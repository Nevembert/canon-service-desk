using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CanonServiceDesk {
    internal static class Program {
        public static string LogsRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CanonServiceDesk","Logs"); } }
        public static string NewFolder() { return Path.Combine(LogsRoot,DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8)); }
        [STAThread] static int Main(string[] args) {
            if(args.Length==2 && args[0]=="--scan") {
                var j=new Journal(Path.GetFullPath(args[1]));
                try { NativeUsb.Scan(j); return 0; }
                catch(Exception e) { j.Add("ERROR","worker.fatal",e.ToString()); return 1; }
            }
            if(args.Length==3 && args[0]=="--test") {
                var j=new Journal(Path.GetFullPath(args[2]));
                try { PrintTest(args[1],j); return 0; }
                catch(Exception e) { j.Add("ERROR","print.failed",e.ToString()); return 1; }
            }
            if(args.Length==3 && args[0]=="--service") {
                var j=new Journal(Path.GetFullPath(args[2]));
                try {
                    var request=Json.Decode<ServiceRequest>(File.ReadAllText(args[1]));
                    if(request.Operation.StartsWith("queue-",StringComparison.Ordinal)) {
                        var result=PrintSpooler.Run(request,j);
                        File.WriteAllText(args[1]+".result.json",Json.Encode(result),Encoding.UTF8);
                    } else NativeUsb.SendBjl(request,j);
                    return 0;
                } catch(Exception e) {
                    var native=e as System.ComponentModel.Win32Exception;
                    j.Add("ERROR","service.failed",e.ToString(),native==null ? null:new { Win32Error=native.NativeErrorCode,Action=Rules.Win32(native.NativeErrorCode) });
                    return 1;
                }
            }
            bool demo=args.Contains("--demo");
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT && !demo) { Console.Error.WriteLine("Windows required. Use --demo only to preview the UI."); return 2; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                var form=new MainForm(demo);
                Application.ThreadException += (s,e)=>form.ShowFailure(e.Exception);
                if(args.Length==3 && args[0]=="--demo" && args[1]=="--screenshot") {
                    var t=new Timer { Interval=1200 };
                    t.Tick+=(s,e)=> { t.Stop(); using(var b=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height)); b.Save(args[2]); } form.Close(); };
                    t.Start();
                }
                if(args.Length==3 && args[0]=="--demo" && args[1]=="--screenshots") {
                    var t=new Timer { Interval=1200 };
                    t.Tick+=(s,e)=> { t.Stop();form.CapturePreviews(args[2]);form.Close(); };t.Start();
                }
                Application.Run(form); return 0;
            } catch(Exception e) { MessageBox.Show(e.ToString(),"Canon Service Desk: ошибка запуска"); return 1; }
        }
        static void PrintTest(string printer,Journal log) {
            var available=new List<string>(); foreach(string n in PrinterSettings.InstalledPrinters) available.Add(n);
            if(!available.Contains(printer)) throw new InvalidOperationException("Выбранная очередь больше не существует. Обновите список.");
            using(var doc=new PrintDocument()) {
                doc.PrinterSettings.PrinterName=printer; doc.DocumentName="Canon Service Desk — пробная страница";
                doc.PrintController=new StandardPrintController();
                if(!doc.PrinterSettings.IsValid) throw new InvalidOperationException("Драйвер выбранной очереди недоступен.");
                doc.PrintPage+=(sender,e)=> {
                    using(var title=new Font("Arial",20,FontStyle.Bold)) using(var normal=new Font("Arial",11)) {
                        float x=e.MarginBounds.Left, y=e.MarginBounds.Top;
                        e.Graphics.DrawString("Canon Service Desk",title,Brushes.Black,x,y); y+=45;
                        e.Graphics.DrawString("Пробная страница • "+DateTime.Now.ToString("yyyy-MM-dd HH:mm"),normal,Brushes.Black,x,y); y+=30;
                        e.Graphics.DrawString(printer,normal,Brushes.Black,x,y); y+=40;
                        e.Graphics.DrawString("Чёрный текст: 0123456789 АБВГД abcdefg",normal,Brushes.Black,x,y); y+=40;
                        Brush[] colors={Brushes.Black,Brushes.Cyan,Brushes.Magenta,Brushes.Yellow};
                        for(int i=0;i<colors.Length;i++) e.Graphics.FillRectangle(colors[i],x+i*100,y,90,60);
                        y+=90;
                        for(int i=0;i<10;i++) e.Graphics.DrawLine(Pens.Black,x,y+i*5,x+390,y+i*5);
                        y+=75; e.Graphics.DrawString("Это проверка обычной печати через драйвер, не тест дюз Canon.",normal,Brushes.Black,x,y);
                    }
                    e.HasMorePages=false;
                };
                log.Add("INFO","print.start","Отправка одной пробной страницы",new { Printer=printer });
                doc.Print();
                log.Add("INFO","print.submitted","Драйвер принял задание. Физический выход страницы программой не подтверждён.");
            }
        }
    }
    public sealed partial class MainForm : Form {
        readonly bool demo;
        Journal journal;
        Snapshot snapshot;
        bool busy;
        readonly Label status=new Label(), location=new Label();
        readonly ListView devices=new ListView();
        readonly TextBox findings=new TextBox(), raw=new TextBox(), events=new TextBox(), note=new TextBox();
        readonly ComboBox queues=new ComboBox();
        readonly Button scan=new Button(), export=new Button(), print=new Button(), preferences=new Button();
        readonly Timer logTimer=new Timer();
        public MainForm(bool demo) {
            this.demo=demo; journal=new Journal(Program.NewFolder());
            Text="Canon Service Desk "+AppInfo.Version+" — диагностика и обслуживание"+(demo ? " [ДЕМО]":"");
            ClientSize=new Size(1120,790); MinimumSize=new Size(1000,740); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi; Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(245,247,251);
            Build();
            logTimer.Interval=600; logTimer.Tick+=(s,e)=>ReadJournal(); logTimer.Start();
            Shown+=(s,e)=> { if(demo) LoadDemo(); else Scan(); };
            FormClosing+=(s,e)=> { if(busy) { e.Cancel=true; MessageBox.Show("Дождитесь завершения проверки (до 30 секунд).","Проверка выполняется"); } };
            FormClosed+=(s,e)=>logTimer.Dispose();
        }
        static Label LabelOf(string text,int size,bool bold) { return new Label { Text=text,AutoSize=true,Font=new Font("Segoe UI",size,bold ? FontStyle.Bold:FontStyle.Regular),ForeColor=Color.FromArgb(25,40,61),Margin=new Padding(0,0,0,8) }; }
        static Button ButtonOf(string text,EventHandler click) { var b=new Button { Text=text,AutoSize=true,Height=36,FlatStyle=FlatStyle.Flat,BackColor=Color.White,Padding=new Padding(8,3,8,3),Margin=new Padding(0,0,10,0) }; b.Click+=click; return b; }
        static TextBox TextArea() { return new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle }; }
        static void ConfigureArea(TextBox b) { b.Multiline=true;b.ReadOnly=true;b.ScrollBars=ScrollBars.Vertical;b.Dock=DockStyle.Fill;b.BackColor=Color.White;b.BorderStyle=BorderStyle.FixedSingle; }
        void Build() {
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); Controls.Add(root);
            var heading=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            heading.Controls.Add(LabelOf("Canon Service Desk",23,true));
            heading.Controls.Add(LabelOf("USB · драйверы · пробная печать · открытый журнал",10,false)); root.Controls.Add(heading,0,0);
            var bar=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false };
            scan.Text="Проверить Canon";scan.AutoSize=true;scan.Height=36;scan.Padding=new Padding(8,3,8,3);scan.Click+=(s,e)=>Scan();bar.Controls.Add(scan);
            export.Text="Сохранить отчёт ZIP";export.AutoSize=true;export.Height=36;export.Padding=new Padding(8,3,8,3);export.Click+=(s,e)=>Export();bar.Controls.Add(export);
            bar.Controls.Add(ButtonOf("Открыть логи",(s,e)=>OpenPath(journal.Folder)));
            bar.Controls.Add(ButtonOf("Запись Service Tool",(s,e)=>TraceHelp())); root.Controls.Add(bar,0,1);
            status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;status.ForeColor=Color.FromArgb(28,86,113);status.Text="Готово к проверке";root.Controls.Add(status,0,2);
            var tabs=new TabControl { Dock=DockStyle.Fill,Padding=new Point(14,7) }; root.Controls.Add(tabs,0,3);
            var overview=new TabPage("Диагностика") { Padding=new Padding(12) };tabs.TabPages.Add(overview);
            var overviewLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3 };
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,160));overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));overview.Controls.Add(overviewLayout);
            devices.View=View.Details;devices.FullRowSelect=true;devices.GridLines=true;devices.Dock=DockStyle.Fill;
            devices.Columns.Add("Устройство",305);devices.Columns.Add("Служба / класс",240);devices.Columns.Add("Состояние драйвера",260);overviewLayout.Controls.Add(devices,0,0);
            ConfigureArea(findings);overviewLayout.Controls.Add(findings,0,1);
            var notePanel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 };
            notePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));notePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            notePanel.Controls.Add(new Label { Text="Ваше наблюдение:",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft },0,0);
            note.Multiline=true;note.Dock=DockStyle.Fill;note.Text="";notePanel.Controls.Add(note,1,0);overviewLayout.Controls.Add(notePanel,0,2);
            BuildServiceTabs(tabs);
            var codes=new TabPage("Коды ошибок") { Padding=new Padding(18) };tabs.TabPages.Add(codes);
            var codeLayout=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            codeLayout.Controls.Add(LabelOf("Код Service Tool и код Windows — разные системы",15,true));
            var choose=new FlowLayoutPanel { Width=900,Height=45 };
            var category=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=220 };category.Items.AddRange(new object[]{"Service Tool","Windows / Win32"});category.SelectedIndex=0;
            var code=new TextBox { Text="006",Width=100 };var explanation=TextArea();explanation.Dock=DockStyle.None;explanation.Width=900;explanation.Height=220;
            EventHandler explain=(s,e)=> { int n; explanation.Text=category.SelectedIndex==0 ? Rules.ServiceTool(code.Text):(int.TryParse(code.Text,out n) ? Rules.Win32(n):"Введите числовой код Windows."); };
            choose.Controls.Add(category);choose.Controls.Add(code);choose.Controls.Add(ButtonOf("Объяснить",explain));codeLayout.Controls.Add(choose);codeLayout.Controls.Add(explanation);explain(null,EventArgs.Empty);
            codeLayout.Controls.Add(new Label { Text="Расшифровка объясняет варианты причин. Она не является результатом проверки вашего принтера.",Width=920,Height=55 });codes.Controls.Add(codeLayout);
            var usb=new TabPage("Ответ USB") { Padding=new Padding(12) };ConfigureArea(raw);raw.Font=new Font("Consolas",10);usb.Controls.Add(raw);tabs.TabPages.Add(usb);
            var log=new TabPage("Журнал") { Padding=new Padding(12) };ConfigureArea(events);events.Font=new Font("Consolas",9);events.WordWrap=false;events.ScrollBars=ScrollBars.Both;log.Controls.Add(events);tabs.TabPages.Add(log);
            location.Dock=DockStyle.Fill;location.TextAlign=ContentAlignment.MiddleLeft;location.Font=new Font("Segoe UI",9);location.ForeColor=Color.DimGray;root.Controls.Add(location,0,4);UpdateState();
        }
        void UpdateState() {
            scan.Enabled=!busy;export.Enabled=!busy;queues.Enabled=!busy;
            print.Enabled=!busy && !demo && queues.Items.Count>0;preferences.Enabled=print.Enabled;
            UpdateServiceState();
            location.Text="Логи сохраняются локально · автоматической отправки нет · "+Path.GetFileName(journal.Folder);
        }
        async void Scan() {
            if(busy || demo) { if(demo) LoadDemo(); return; }
            busy=true;journal=new Journal(Program.NewFolder());snapshot=null;devices.Items.Clear();findings.Clear();raw.Clear();events.Clear();jobs.Items.Clear();queueReport=null;jobQueues.Items.Clear();bjlDevices.Items.Clear();UpdateState();
            status.Text="Проверяю Canon… До 30 секунд. Другие программы принтера лучше закрыть.";
            journal.Add("INFO","app.scan","Начало проверки",new { Version=AppInfo.Version,Architecture=IntPtr.Size*8,Observation=note.Text });
            try {
                int exit=await RunWorker("--scan "+WindowsArgs.Quote(journal.Folder),30000);
                string path=Path.Combine(journal.Folder,"report.json");
                snapshot=File.Exists(path) ? Json.Decode<Snapshot>(File.ReadAllText(path)):new Snapshot();
                if(exit!=0) { snapshot.Completed=false;snapshot.Errors.Add(exit==-1 ? "Проверка прервана по таймауту 30 секунд.":"Процесс проверки завершился с кодом "+exit);journal.Save(snapshot); }
                Render();status.Text=snapshot.Completed ? "Проверка завершена. Результаты и дальнейшие действия — ниже.":"Получен частичный отчёт. Подробности сохранены в журнале.";
            } catch(Exception e) { ShowFailure(e); }
            finally { busy=false;UpdateState();ReadJournal(); }
        }
        Task<int> RunWorker(string arguments,int timeout) {
            return Task.Run(()=> {
                var info=new ProcessStartInfo(Application.ExecutablePath,arguments) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory };
                using(var p=Process.Start(info)) {
                    if(!p.WaitForExit(timeout)) {
                        try { p.Kill();p.WaitForExit(3000); } catch(InvalidOperationException) { }
                        journal.Add("ERROR","worker.timeout","Запрос не завершился за отведённое время; процесс проверки остановлен.");
                        return -1;
                    }
                    return p.ExitCode;
                }
            });
        }
        void Render() {
            devices.Items.Clear();queues.Items.Clear();
            foreach(var d in snapshot.Devices) {
                string state=d.ProblemCodeKnown ? (d.ProblemCode==0 ? "Windows не сообщает ошибок":"Код "+d.ProblemCode):"Состояние не прочитано";
                var row=new ListViewItem(d.Name);row.SubItems.Add(d.Service+" / "+d.Class);row.SubItems.Add(state);devices.Items.Add(row);
            }
            foreach(string q in snapshot.Printers) queues.Items.Add(q);if(queues.Items.Count>0) queues.SelectedIndex=0;
            findings.Text=string.Join("\r\n\r\n",Rules.Diagnose(snapshot).Select(f=>f.ToString()));
            var b=new StringBuilder();foreach(var u in snapshot.Usb) {
                b.AppendLine(u.Name).AppendLine("PnP: "+u.InstanceId).AppendLine("Путь: "+u.Path).AppendLine("Модель: "+u.Model).AppendLine("IEEE 1284: "+u.DeviceId).AppendLine("HEX: "+u.RawHex).AppendLine("Чтение: "+u.ReadOk+"; Win32: "+u.Win32Error+"; этап: "+u.ErrorStage).AppendLine();
            }
            raw.Text=b.ToString();RefreshServiceDevices();UpdateState();
        }
        void ReadJournal() {
            string p=Path.Combine(journal.Folder,"journal.txt");if(!File.Exists(p)) return;
            try {
                string text;using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) using(var r=new StreamReader(f,Encoding.UTF8)) text=r.ReadToEnd();
                if(text.Length>180000) text="[В окне показан конец журнала; полный файл сохранён.]\r\n"+text.Substring(text.Length-180000);
                if(events.Text!=text) { events.Text=text;events.SelectionStart=events.TextLength;events.ScrollToCaret(); }
            } catch(IOException) { }
        }
        void Export() {
            if(busy) return;
            using(var dlg=new SaveFileDialog { Filter="ZIP отчёт (*.zip)|*.zip",FileName="Canon-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip" }) {
                if(dlg.ShowDialog()!=DialogResult.OK) return;
                try {
                    string dest=Path.GetFullPath(dlg.FileName);
                    if(dest.StartsWith(Path.GetFullPath(journal.Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Сохраните ZIP вне папки текущего журнала.");
                    File.WriteAllText(Path.Combine(journal.Folder,"observation.txt"),note.Text,Encoding.UTF8);
                    File.WriteAllText(Path.Combine(journal.Folder,"summary.txt"),"Canon Service Desk "+AppInfo.Version+"\r\n"+findings.Text+"\r\n\r\nОтчёт может содержать серийный номер принтера и пути USB. Никакие данные автоматически не публикуются.",Encoding.UTF8);
                    File.WriteAllText(Path.Combine(journal.Folder,"service-history.json"),Json.Encode(Entries()),Encoding.UTF8);
                    string temp=dest+".tmp-"+Guid.NewGuid().ToString("N");
                    try { ZipFile.CreateFromDirectory(journal.Folder,temp,CompressionLevel.Optimal,false);if(File.Exists(dest)) File.Delete(dest);File.Move(temp,dest); }
                    finally { if(File.Exists(temp)) File.Delete(temp); }
                    status.Text="Отчёт сохранён: "+dest;
                } catch(Exception e) { ShowFailure(e); }
            }
        }
        async void TestPrint() {
            if(busy || demo || queues.SelectedItem==null) return;
            busy=true;UpdateState();status.Text="Отправляю одну пробную страницу…";
            try { int exit=await RunWorker("--test "+WindowsArgs.Quote(queues.SelectedItem.ToString())+" "+WindowsArgs.Quote(journal.Folder),30000);status.Text=exit==0 ? "Задание передано драйверу. Проверьте, вышел ли лист.":"Печать не подтверждена. Подробности в журнале; перед повтором проверьте очередь."; }
            catch(Exception e) { ShowFailure(e); } finally { busy=false;UpdateState();ReadJournal(); }
        }
        void OpenPreferences() {
            if(queues.SelectedItem==null || busy || demo) return;
            try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"rundll32.exe"),"printui.dll,PrintUIEntry /e /n "+WindowsArgs.Quote(queues.SelectedItem.ToString())) { UseShellExecute=false });journal.Add("INFO","driver.ui","Открыто окно настроек драйвера."); }
            catch(Exception e) { ShowFailure(e); }
        }
        void TraceHelp() { OpenPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trace","README-RU.txt")); }
        void OpenPath(string path) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); } catch(Exception e) { ShowFailure(e); } }
        public void ShowFailure(Exception e) {
            status.Text="Ошибка: "+e.Message;
            try { journal.Add("ERROR","app",e.ToString()); } catch(Exception logError) { MessageBox.Show(e.Message+"\r\nНе удалось записать журнал: "+logError.Message,"Ошибка");return; }
            MessageBox.Show(e.Message+"\r\nПодробности сохранены в журнале.","Canon Service Desk");
        }
        void LoadDemo() {
            snapshot=new Snapshot { Completed=true,Demo=true };
            snapshot.Devices.Add(new Device { Name="Canon Device (пример)",Service="usbprint",Class="USB",ProblemCodeKnown=true,InstanceId="USB\\VID_04A9&PID_DEMO\\EXAMPLE" });
            snapshot.Usb.Add(new UsbResult { Name="Canon Device (пример)",ReadOk=true,Model="G2010 series",DeviceId="MFG:Canon;MDL:G2010 series;",RawHex="4D 46 47 3A 43 61 6E 6F 6E" });
            snapshot.Printers.Add("Canon G2010 series (пример)");journal.Save(snapshot);
            journal.Add("INFO","demo","Демонстрационный режим: USB и печать не вызываются.");Render();status.Text="ДЕМО — пример интерфейса, не результат проверки принтера.";
        }
    }
}
