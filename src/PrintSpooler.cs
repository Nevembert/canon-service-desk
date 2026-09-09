using System;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;

namespace CanonServiceDesk {
    public static class PrintSpooler {
        [StructLayout(LayoutKind.Sequential)] public struct SystemTime {
            public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;
            public override string ToString() { return string.Format("{0:D4}-{1:D2}-{2:D2}T{3:D2}:{4:D2}:{5:D2}.{6:D3}Z",Year,Month,Day,Hour,Minute,Second,Milliseconds); }
        }
        [StructLayout(LayoutKind.Sequential)] public struct JobInfo1 {
            public uint Id;
            public IntPtr Printer,Machine,User,Document,Datatype,StatusText;
            public uint Status,Priority,Position,TotalPages,PagesPrinted;
            public SystemTime Submitted;
        }
        [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool OpenPrinterW(string name,out IntPtr handle,IntPtr defaults);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool EnumJobsW(IntPtr handle,uint first,uint count,uint level,IntPtr buffer,uint capacity,out uint needed,out uint returned);
        [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetJobW(IntPtr handle,uint id,uint level,IntPtr job,uint command);
        static string Text(IntPtr p) { return p==IntPtr.Zero ? "":Marshal.PtrToStringUni(p); }
        public static string StatusText(uint state) {
            string[] names={"Пауза","Ошибка","Удаляется","Приём данных","Печатается","Нет связи","Нет бумаги","Напечатано","Удалено","Заблокировано","Нужна помощь","Завершено","Сохранено","Рендеринг"};
            var selected=names.Where((n,i)=>(state & (1u<<i))!=0).ToArray();
            return selected.Length==0 ? "В очереди / статус не уточнён":string.Join(", ",selected);
        }
        public static bool SameJob(QueueJob expected,QueueJob actual) {
            return expected!=null && actual!=null && expected.Id==actual.Id && expected.Document==actual.Document && expected.Owner==actual.Owner && expected.Submitted==actual.Submitted;
        }
        static IntPtr Open(string printer) {
            bool installed=PrinterSettings.InstalledPrinters.Cast<string>().Contains(printer);
            bool canon=printer.IndexOf("Canon",StringComparison.OrdinalIgnoreCase)>=0 || printer.IndexOf("G2010",StringComparison.OrdinalIgnoreCase)>=0;
            if(!installed || !canon) throw new InvalidOperationException("Выбранная очередь Canon больше не установлена. Обновите диагностику.");
            IntPtr handle;
            if(!OpenPrinterW(printer,out handle,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return handle;
        }
        static QueueReport Read(IntPtr handle,string printer) {
            var report=new QueueReport { Printer=printer };
            uint needed,count;
            bool ok=EnumJobsW(handle,0,1000,1,IntPtr.Zero,0,out needed,out count);
            int error=ok ? 0:Marshal.GetLastWin32Error();
            if(!ok && error!=122) throw new Win32Exception(error);
            if(needed==0) { report.Complete=true;return report; }
            for(int attempt=0;attempt<3;attempt++) {
                if(needed>16777216) throw new InvalidOperationException("Список очереди превышает 16 МБ. Откройте очередь средствами Windows.");
                uint capacity=needed;IntPtr buffer=Marshal.AllocHGlobal((int)capacity);
                try {
                    if(!EnumJobsW(handle,0,1000,1,buffer,capacity,out needed,out count)) {
                        error=Marshal.GetLastWin32Error();
                        if(error==122 && attempt<2) continue;
                        throw new Win32Exception(error);
                    }
                    int stride=Marshal.SizeOf(typeof(JobInfo1));
                    if((long)stride*count>capacity) throw new InvalidOperationException("Windows вернула некорректный размер списка заданий.");
                    for(uint i=0;i<count;i++) {
                        var j=(JobInfo1)Marshal.PtrToStructure(IntPtr.Add(buffer,checked((int)i*stride)),typeof(JobInfo1));
                        report.Jobs.Add(new QueueJob { Id=j.Id,Document=Text(j.Document),Owner=Text(j.User),Submitted=j.Submitted.ToString(),Status=j.Status,TotalPages=j.TotalPages,PagesPrinted=j.PagesPrinted,State=Text(j.StatusText) });
                        if(report.Jobs.Last().State.Length==0) report.Jobs.Last().State=StatusText(j.Status);
                    }
                    report.Complete=count<1000;
                    if(!report.Complete) report.Error="Показаны первые 1000 заданий.";
                    return report;
                } finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException("Очередь меняется во время чтения. Обновите список.");
        }
        public static QueueReport Run(ServiceRequest request,Journal log) {
            uint command;
            switch(request.Operation) {
                case "queue-list": command=0;break;
                case "queue-pause": command=1;break;
                case "queue-resume": command=2;break;
                case "queue-delete": command=5;break; // JOB_CONTROL_DELETE, not deprecated CANCEL.
                default: throw new ArgumentException("Неизвестная операция очереди.");
            }
            IntPtr handle=Open(request.Printer);
            try {
                if(command!=0) {
                    var current=Read(handle,request.Printer);
                    var actual=current.Jobs.FirstOrDefault(x=>request.Job!=null && x.Id==request.Job.Id);
                    if(!SameJob(request.Job,actual)) throw new InvalidOperationException("Задание исчезло или изменилось. Обновите очередь и выберите его заново.");
                    log.Add("INFO","queue.request",request.Operation,new { request.Printer,Job=actual,Command=command });
                    if(!SetJobW(handle,actual.Id,0,IntPtr.Zero,command)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    log.Add("INFO","queue.accepted","Windows приняла команду. Уже переданные принтеру страницы могут допечататься.");
                }
                var report=Read(handle,request.Printer);log.Add("INFO","queue.report","Состояние очереди",report);return report;
            } finally { ClosePrinter(handle); }
        }
    }
}
