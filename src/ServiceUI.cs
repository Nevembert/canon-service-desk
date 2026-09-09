using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CanonServiceDesk {
    public sealed partial class MainForm {
        readonly ListBox procedures=new ListBox();
        readonly TextBox procedureText=new TextBox(),counterInput=new TextBox(),counterOutput=new TextBox(),deviceKey=new TextBox();
        readonly ListView jobs=new ListView();
        readonly ComboBox jobQueues=new ComboBox(),bjlDevices=new ComboBox(),bjlOperation=new ComboBox();
        readonly Button wizardButton=new Button(),queueRefresh=new Button(),queuePause=new Button(),queueResume=new Button(),queueDelete=new Button(),bjlSend=new Button();
        readonly Label queueState=new Label(),bjlState=new Label();
        readonly CheckBox bjlConfirm=new CheckBox();
        readonly List<Control> serviceActions=new List<Control>();
        readonly ServiceHistory history=new ServiceHistory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CanonServiceDesk","History"));
        List<ServiceEntry> demoHistory=new List<ServiceEntry>();
        QueueReport queueReport;
        TabControl mainTabs;

        static TableLayoutPanel Grid(int rows) {
            var p=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=rows };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            return p;
        }
        static FlowLayoutPanel Buttons() { return new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=new Padding(0,5,0,5) }; }
        Button ActionButton(string title,EventHandler action) { var b=ButtonOf(title,action);serviceActions.Add(b);return b; }
        void SetButton(Button button,string text,EventHandler click) { button.Text=text;button.AutoSize=true;button.Padding=new Padding(6,4,6,4);button.Click+=click; }
        void BuildServiceTabs(TabControl tabs) {
            mainTabs=tabs;
            var page=new TabPage("Обслуживание") { Padding=new Padding(12) };tabs.TabPages.Add(page);
            var layout=Grid(3);layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.Controls.Add(layout);
            layout.Controls.Add(new Label { AutoSize=true,Text="Процедуры для G2411 / G2010 series. Мастер показывает действия на панели или в Canon IJ; результат записывается с ваших слов.",MaximumSize=new Size(1000,0),Margin=new Padding(0,0,0,10) },0,0);
            var top=Buttons();queues.Width=310;queues.DropDownStyle=ComboBoxStyle.DropDownList;top.Controls.Add(queues);
            SetButton(print,"Пробная страница",(s,e)=>TestPrint());top.Controls.Add(print);
            SetButton(preferences,"Открыть Canon IJ / драйвер",(s,e)=>OpenPreferences());top.Controls.Add(preferences);layout.Controls.Add(top,0,1);
            var columns=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,310));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.Controls.Add(columns,0,2);
            procedures.Dock=DockStyle.Fill;procedures.IntegralHeight=false;procedures.Items.AddRange(ServiceCatalog.All.Cast<object>().ToArray());columns.Controls.Add(procedures,0,0);
            var right=Grid(2);right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.AutoSize));columns.Controls.Add(right,1,0);
            ConfigureArea(procedureText);right.Controls.Add(procedureText,0,0);
            var actions=Buttons();SetButton(wizardButton,"Пошаговый мастер",(s,e)=>StartProcedure());actions.Controls.Add(wizardButton);
            actions.Controls.Add(ButtonOf("Источник / инструкция",(s,e)=> { var p=procedures.SelectedItem as ServiceProcedure;if(p!=null) OpenPath(p.Source); }));right.Controls.Add(actions,0,1);
            procedures.SelectedIndexChanged+=(s,e)=> {
                var p=procedures.SelectedItem as ServiceProcedure;
                if(p==null) return;
                procedureText.Text=p.Title+"\r\n"+p.Method+"\r\n\r\n"+p.Description+"\r\n\r\n"+string.Join("\r\n\r\n",p.Steps.Select((t,i)=>(i+1)+". "+t));
                wizardButton.Enabled=!busy && p.Id!="page-reset";
                wizardButton.Text=p.Id=="page-reset" ? "USB-сброс недоступен":"Пошаговый мастер";
            };
            procedures.SelectedIndex=0;
            BuildCountersTab(tabs);BuildQueueTab(tabs);BuildBjlTab(tabs);
        }
        void BuildCountersTab(TabControl tabs) {
            var page=new TabPage("Счётчики") { Padding=new Padding(12) };tabs.TabPages.Add(page);
            var layout=Grid(5);page.Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,130));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(1000,0),Text="Показания с вашего сервисного листа / текстового отчёта. Чтение и обнуление аппаратного пробега по USB не реализованы. Отсчёт после обслуживания хранится только в приложении.",Margin=new Padding(0,0,0,8) },0,0);
            var name=Buttons();name.Controls.Add(new Label { Text="Устройство / серийный номер:",AutoSize=true,Padding=new Padding(0,6,0,0) });
            deviceKey.Text="Мой G2411";deviceKey.Width=310;name.Controls.Add(deviceKey);name.Controls.Add(ActionButton("Показать историю",(s,e)=>RefreshCounters()));layout.Controls.Add(name,0,1);
            counterInput.Multiline=true;counterInput.ScrollBars=ScrollBars.Vertical;counterInput.Dock=DockStyle.Fill;counterInput.Font=new Font("Consolas",10);layout.Controls.Add(counterInput,0,2);
            var buttons=Buttons();buttons.Controls.Add(ActionButton("Открыть текст отчёта",(s,e)=>ImportCounters()));buttons.Controls.Add(ActionButton("Сохранить показания",(s,e)=>SaveCounters()));buttons.Controls.Add(ActionButton("Начать отсчёт после обслуживания",(s,e)=>SetBaseline()));layout.Controls.Add(buttons,0,3);
            ConfigureArea(counterOutput);layout.Controls.Add(counterOutput,0,4);
            counterOutput.Text="Вставьте строки со своего листа, например:\r\nTotal 7522 Pages\r\nPrint 7469 Pages\r\nBlank 53 Pages\r\n\r\nПоддерживается также TPAGE=7522. Это пример формата, не показания вашего принтера. Фото и двоичный EEPROM здесь не распознаются.";
        }
        void BuildQueueTab(TabControl tabs) {
            var page=new TabPage("Очередь печати") { Padding=new Padding(12) };tabs.TabPages.Add(page);
            var layout=Grid(4);page.Controls.Add(layout);layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(1000,0),Text="Задания выбранной очереди Windows. Количество страниц в задании не является общим пробегом принтера.",Margin=new Padding(0,0,0,10) },0,0);
            var row=Buttons();jobQueues.DropDownStyle=ComboBoxStyle.DropDownList;jobQueues.Width=330;row.Controls.Add(jobQueues);
            SetButton(queueRefresh,"Обновить",(s,e)=>QueueAction("queue-list"));row.Controls.Add(queueRefresh);
            SetButton(queuePause,"Пауза",(s,e)=>QueueAction("queue-pause"));row.Controls.Add(queuePause);
            SetButton(queueResume,"Продолжить",(s,e)=>QueueAction("queue-resume"));row.Controls.Add(queueResume);
            SetButton(queueDelete,"Удалить задание",(s,e)=>QueueAction("queue-delete"));row.Controls.Add(queueDelete);layout.Controls.Add(row,0,1);
            jobs.Dock=DockStyle.Fill;jobs.View=View.Details;jobs.FullRowSelect=true;jobs.MultiSelect=false;jobs.HideSelection=false;jobs.GridLines=true;
            jobs.Columns.Add("ID",70);jobs.Columns.Add("Документ",300);jobs.Columns.Add("Состояние",270);jobs.Columns.Add("Страниц",90);jobs.Columns.Add("Передано",90);layout.Controls.Add(jobs,0,2);
            queueState.Text="Выберите очередь и нажмите «Обновить».";queueState.AutoSize=true;queueState.MaximumSize=new Size(1000,0);layout.Controls.Add(queueState,0,3);
            jobQueues.SelectedIndexChanged+=(s,e)=> { jobs.Items.Clear();queueReport=null;queueState.Text="Очередь изменена. Обновите список заданий.";UpdateServiceState(); };
            jobs.SelectedIndexChanged+=(s,e)=>UpdateServiceState();
        }
        void BuildBjlTab(TabControl tabs) {
            var page=new TabPage("BJL · опытный") { Padding=new Padding(15) };tabs.TabPages.Add(page);
            var layout=Grid(6);page.Controls.Add(layout);
            for(int i=0;i<5;i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(970,0),Text="Прямая отправка команд Canon BJL без ключей. Поддержка конкретных операций на G2411 не проверена. Доступно только устройству Canon, которое явно объявляет BJL в строке CMD. В сервисном режиме не использовать.",Margin=new Padding(0,0,0,15) },0,0);
            bjlDevices.DropDownStyle=ComboBoxStyle.DropDownList;bjlDevices.Width=650;layout.Controls.Add(bjlDevices,0,1);
            bjlOperation.DropDownStyle=ComboBoxStyle.DropDownList;bjlOperation.Width=430;bjlOperation.Items.AddRange(new object[]{"Тест дюз (один лист A4)","Обычная очистка — все цвета","Обычная очистка — чёрный"});bjlOperation.SelectedIndex=0;layout.Controls.Add(bjlOperation,0,2);
            bjlConfirm.AutoSize=true;bjlConfirm.MaximumSize=new Size(950,0);bjlConfirm.Text="Принтер в обычном режиме, другие программы закрыты, чернил достаточно. Для теста загружен A4.";bjlConfirm.Margin=new Padding(0,12,0,12);layout.Controls.Add(bjlConfirm,0,3);
            SetButton(bjlSend,"Отправить выбранную команду",(s,e)=>SendBjl());layout.Controls.Add(bjlSend,0,4);
            bjlState.AutoSize=true;bjlState.MaximumSize=new Size(950,0);bjlState.Text="Сначала выполните диагностику. При отсутствии CMD:BJL кнопка отправки недоступна.";layout.Controls.Add(bjlState,0,5);
            bjlConfirm.CheckedChanged+=(s,e)=>UpdateServiceState();bjlDevices.SelectedIndexChanged+=(s,e)=>UpdateServiceState();
        }
        void UpdateServiceState() {
            foreach(var c in serviceActions) c.Enabled=!busy && !demo;
            wizardButton.Enabled=!busy && procedures.SelectedItem!=null && ((ServiceProcedure)procedures.SelectedItem).Id!="page-reset";procedures.Enabled=!busy;deviceKey.Enabled=!busy;counterInput.Enabled=!busy;
            jobQueues.Enabled=!busy;
            queueRefresh.Enabled=!busy && !demo && jobQueues.SelectedItem!=null;
            bool canEdit=!busy && !demo && queueReport!=null && queueReport.Printer==(jobQueues.SelectedItem as string) && jobs.SelectedItems.Count==1;
            queuePause.Enabled=canEdit;queueResume.Enabled=canEdit;queueDelete.Enabled=canEdit;
            bjlDevices.Enabled=!busy;bjlOperation.Enabled=!busy;bjlConfirm.Enabled=!busy;
            bjlSend.Enabled=!busy && !demo && bjlConfirm.Checked && bjlDevices.SelectedIndex>=0;
        }
        void RefreshServiceDevices() {
            jobQueues.Items.Clear();bjlDevices.Items.Clear();bjlConfirm.Checked=false;
            if(snapshot==null) return;
            foreach(var q in snapshot.Printers) jobQueues.Items.Add(q);
            if(jobQueues.Items.Count>0) jobQueues.SelectedIndex=0;
            foreach(var u in snapshot.Usb.Where(u=>u.ReadOk && BjlCommands.Advertised(u.DeviceId))) bjlDevices.Items.Add(new UsbChoice(u));
            if(bjlDevices.Items.Count>0) bjlDevices.SelectedIndex=0;
            bjlState.Text=bjlDevices.Items.Count>0 ? "BJL заявлен устройством. При отправке идентификатор будет прочитан заново. Получение байтов не подтверждает выполнение процедуры.":"Canon с явно заявленным CMD:BJL не найден. Используйте панель принтера или Canon IJ во вкладке «Обслуживание».";
            UpdateServiceState();
        }
        sealed class UsbChoice {
            public readonly UsbResult Device;
            public UsbChoice(UsbResult d) { Device=d; }
            public override string ToString() { return Device.Model+" — "+Device.InstanceId; }
        }
        void StartProcedure() {
            if(busy) return;var p=procedures.SelectedItem as ServiceProcedure;if(p==null || p.Id=="page-reset") return;
            if(!demo && string.IsNullOrWhiteSpace(deviceKey.Text)) { MessageBox.Show(this,"Сначала укажите устройство на вкладке «Счётчики», чтобы сохранить результат процедуры.");return; }
            using(var wizard=new ProcedureForm(p,demo)) {
                wizard.ShowDialog(this);
                if(demo || !wizard.Started) return;
                try {
                    var record=new ServiceEntry { Device=deviceKey.Text.Trim(),Kind="procedure",Operation=p.Id,Outcome=wizard.Outcome,Note=wizard.ResultNote,Source="user_report" };
                    history.Append(record);journal.Add("INFO","service.user_report","Результат процедуры со слов пользователя",record);
                    status.Text="Результат сохранён в истории устройства и журнале. Автоматическое подтверждение не выполнялось.";
                } catch(Exception e) { ShowFailure(e); }
            }
        }
        void ImportCounters() {
            if(busy || demo) return;
            using(var dlg=new OpenFileDialog { Filter="Текстовый отчёт (*.txt)|*.txt",Title="Открыть текст сервисного отчёта" }) {
                if(dlg.ShowDialog()!=DialogResult.OK) return;
                try { if(new FileInfo(dlg.FileName).Length>65536) throw new InvalidOperationException("Файл превышает 64 КБ.");counterInput.Text=File.ReadAllText(dlg.FileName); }
                catch(Exception e) { ShowFailure(e); }
            }
        }
        List<ServiceEntry> Entries() { return demo ? demoHistory:history.Read(deviceKey.Text); }
        void SaveCounters() {
            if(busy || demo) return;
            try {
                var reading=CounterParser.Parse(counterInput.Text);
                var entry=new ServiceEntry { Device=deviceKey.Text.Trim(),Kind="reading",Operation="counter-record",Counters=reading,Source="operator_text" };
                history.Append(entry);journal.Add("INFO","counter.record","Показания из введённого пользователем текста",entry);RefreshCounters();
            } catch(Exception e) { ShowFailure(e); }
        }
        void SetBaseline() {
            if(busy || demo) return;
            try {
                var rows=Entries();var reading=rows.LastOrDefault(x=>x.Kind=="reading" && x.Counters!=null && x.Counters.Total.HasValue);
                if(reading==null) throw new InvalidOperationException("Сначала сохраните показания с числом Total.");
                if(MessageBox.Show(this,"Начать локальный отсчёт для «"+deviceKey.Text+"» от Total="+reading.Counters.Total+"?\r\nОбщий пробег в принтере не изменится.","Отметка после обслуживания",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
                var entry=new ServiceEntry { Device=deviceKey.Text.Trim(),Kind="baseline",Operation="local-baseline",Counters=reading.Counters,Source="app_local" };
                history.Append(entry);journal.Add("INFO","counter.local_baseline","Создана локальная отметка; аппаратный счётчик не изменялся.",entry);RefreshCounters();
            } catch(Exception e) { ShowFailure(e); }
        }
        void RefreshCounters() {
            try {
                var rows=Entries();var readings=rows.Where(x=>x.Kind=="reading" && x.Counters!=null).ToList();var last=readings.LastOrDefault();
                var baseline=rows.LastOrDefault(x=>x.Kind=="baseline" && x.Counters!=null);
                var text=new StringBuilder("Устройство: "+deviceKey.Text+"\r\nИсточник: текст / наблюдения пользователя. EEPROM не читается и не изменяется.\r\n\r\n");
                if(last!=null) text.AppendLine("Последние показания: Total="+CounterParser.Display(last.Counters.Total)+"; Print="+CounterParser.Display(last.Counters.Printed)+"; Blank="+CounterParser.Display(last.Counters.Blank));
                else text.AppendLine("Сохранённых показаний пока нет.");
                if(readings.Count>1) text.AppendLine(CounterParser.Delta(readings[readings.Count-2].Counters,last.Counters));
                if(baseline!=null && last!=null) text.AppendLine("После локальной отметки обслуживания: "+CounterParser.Delta(baseline.Counters,last.Counters));
                text.AppendLine().AppendLine("История (UTC):");
                foreach(var entry in rows.Skip(Math.Max(0,rows.Count-100)).Reverse()) text.AppendLine(entry.CreatedUtc+" | "+entry.Kind+" | "+entry.Operation+" | "+entry.Outcome+(entry.Counters==null ? "":" | Total="+CounterParser.Display(entry.Counters.Total))+" | "+entry.Note);
                counterOutput.Text=text.ToString();
            } catch(Exception e) { ShowFailure(e); }
        }
        async void QueueAction(string operation) {
            if(busy || demo || jobQueues.SelectedItem==null) return;
            var request=new ServiceRequest { Printer=jobQueues.SelectedItem.ToString(),Operation=operation };
            if(operation!="queue-list") {
                if(jobs.SelectedItems.Count!=1 || queueReport==null || queueReport.Printer!=request.Printer) return;
                request.Job=(QueueJob)jobs.SelectedItems[0].Tag;
                if(operation=="queue-delete" && MessageBox.Show(this,"Удалить задание №"+request.Job.Id+" «"+request.Job.Document+"» из очереди «"+request.Printer+"»?\r\nУже переданные принтеру страницы могут допечататься.","Удаление задания",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
            }
            busy=true;UpdateState();queueState.Text="Запрос к очереди Windows…";
            try {
                string path=Path.Combine(journal.Folder,"service-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllText(path,Json.Encode(request),Encoding.UTF8);
                int exit=await RunWorker("--service "+WindowsArgs.Quote(path)+" "+WindowsArgs.Quote(journal.Folder),30000);
                jobs.Items.Clear();queueReport=null;
                if(exit!=0) { queueState.Text="Команда не подтверждена. Обновите очередь; подробности в журнале.";return; }
                queueReport=Json.Decode<QueueReport>(File.ReadAllText(path+".result.json"));
                foreach(var j in queueReport.Jobs) { var row=new ListViewItem(j.Id.ToString()) { Tag=j };row.SubItems.Add(j.Document);row.SubItems.Add(j.State);row.SubItems.Add(j.TotalPages==0 ? "неизвестно":j.TotalPages.ToString());row.SubItems.Add(j.PagesPrinted.ToString());jobs.Items.Add(row); }
                queueState.Text="Заданий: "+queueReport.Jobs.Count+". "+queueReport.Error+" Показания передал диспетчер печати Windows.";
            } catch(Exception e) { ShowFailure(e); } finally { busy=false;UpdateState();ReadJournal(); }
        }
        async void SendBjl() {
            if(busy || demo || !bjlConfirm.Checked || bjlDevices.SelectedItem==null) return;
            var target=((UsbChoice)bjlDevices.SelectedItem).Device;
            string operation=new[]{"nozzle","clean-all","clean-black"}[bjlOperation.SelectedIndex];
            if(MessageBox.Show(this,"Отправить «"+bjlOperation.Text+"» на "+target.Model+"?\r\nОчистка расходует чернила. Поддержка этой операции на вашем устройстве ещё не проверена.","Экспериментальная команда BJL",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
            busy=true;UpdateState();bjlState.Text="Повторно проверяю USB и передаю команду…";
            try {
                string path=Path.Combine(journal.Folder,"service-"+Guid.NewGuid().ToString("N")+".json");
                File.WriteAllText(path,Json.Encode(new ServiceRequest { Operation=operation,UsbPath=target.Path,DeviceId=target.DeviceId }),Encoding.UTF8);
                int exit=await RunWorker("--service "+WindowsArgs.Quote(path)+" "+WindowsArgs.Quote(journal.Folder),30000);
                bjlState.Text=exit==0 ? "Байты команды переданы. Дождитесь окончания работы принтера и проверьте результат; автоматического подтверждения выполнения нет.":"Передача не подтверждена. Не повторяйте до проверки панели принтера и журнала.";
            } catch(Exception e) { ShowFailure(e); } finally { busy=false;bjlConfirm.Checked=false;UpdateState();ReadJournal(); }
        }
        public void PreviewTab(string name) {
            if(!demo) return;
            foreach(TabPage page in mainTabs.TabPages) if(page.Text==name) { mainTabs.SelectedTab=page;return; }
            throw new ArgumentException("Нет вкладки: "+name);
        }
        public void CapturePreviews(string directory) {
            if(!demo) throw new InvalidOperationException("Снимки доступны только в демо.");
            Directory.CreateDirectory(directory);
            counterInput.Text="Total 7522 Pages\r\nPrint 7469 Pages\r\nBlank 53 Pages";
            demoHistory=new List<ServiceEntry> { new ServiceEntry { Device="Демонстрация",Kind="reading",Counters=CounterParser.Parse(counterInput.Text),Source="demo" } };
            deviceKey.Text="Демонстрация";RefreshCounters();
            queueState.Text="ДЕМО: очередь Windows не опрашивалась.";
            var example=new ListViewItem("12");example.SubItems.Add("Пример документа");example.SubItems.Add("Нет бумаги");example.SubItems.Add("1");example.SubItems.Add("0");jobs.Items.Add(example);
            for(int i=0;i<mainTabs.TabPages.Count;i++) {
                mainTabs.SelectedIndex=i;Application.DoEvents();
                using(var b=new Bitmap(Width,Height)) { DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(directory,"tab-"+i+".png")); }
            }
            using(var wizard=new ProcedureForm(ServiceCatalog.All[0],true)) {
                wizard.Show(this);Application.DoEvents();
                using(var b=new Bitmap(wizard.Width,wizard.Height)) { wizard.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(directory,"absorber-wizard.png")); }
                wizard.Close();
            }
        }
    }

    public sealed class ProcedureForm : Form {
        readonly ServiceProcedure procedure;
        readonly Label stepTitle=new Label();
        readonly TextBox instruction=new TextBox(),note=new TextBox();
        readonly ComboBox result=new ComboBox();
        readonly Button next=new Button();
        int position;
        public bool Started { get;private set; }
        public string Outcome { get;private set; }
        public string ResultNote { get { return "Шаг "+(position+1)+" из "+procedure.Steps.Length+". "+note.Text; } }
        public ProcedureForm(ServiceProcedure procedure,bool demo) {
            this.procedure=procedure;Outcome="Прервано";
            Text=procedure.Title+(demo ? " [ДЕМО]":"");ClientSize=new Size(720,440);MinimumSize=new Size(620,400);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(18),RowCount=5,ColumnCount=1 };Controls.Add(root);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,65));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stepTitle.AutoSize=true;stepTitle.Font=new Font(Font,FontStyle.Bold);stepTitle.Margin=new Padding(0,0,0,12);root.Controls.Add(stepTitle,0,0);
            instruction.ReadOnly=true;instruction.Multiline=true;instruction.Dock=DockStyle.Fill;instruction.ScrollBars=ScrollBars.Vertical;instruction.BackColor=Color.White;root.Controls.Add(instruction,0,1);
            result.DropDownStyle=ComboBoxStyle.DropDownList;result.Width=580;result.Items.AddRange(new object[]{"Выберите фактический результат…","Выполнено: результат устраивает","Выполнено: проблема осталась","Не удалось выполнить"});result.SelectedIndex=0;root.Controls.Add(result,0,2);
            note.Multiline=true;note.Dock=DockStyle.Fill;root.Controls.Add(note,0,3);
            var buttons=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            var back=new Button { Text="Назад",AutoSize=true };back.Click+=(s,e)=> { if(position>0) { position--;RenderStep(); } };buttons.Controls.Add(back);
            next.AutoSize=true;next.Click+=(s,e)=> {
                if(position<procedure.Steps.Length-1) { Started=true;position++;RenderStep();return; }
                if(result.SelectedIndex==0) { MessageBox.Show(this,"Выберите наблюдаемый результат. Пустое поле ниже можно использовать для заметки.");return; }
                Started=true;Outcome=result.Text;DialogResult=DialogResult.OK;Close();
            };buttons.Controls.Add(next);
            var cancel=new Button { Text="Прервать",AutoSize=true };cancel.Click+=(s,e)=>Close();buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,4);
            RenderStep();
        }
        void RenderStep() {
            stepTitle.Text="Шаг "+(position+1)+" / "+procedure.Steps.Length+" · "+procedure.Method;
            instruction.Text=procedure.Steps[position];bool last=position==procedure.Steps.Length-1;
            result.Enabled=last;next.Text=last ? "Записать мой результат":"Шаг выполнен →";
        }
    }
}
