// Runs only against the isolated, paused CI queue created by queue-integration.ps1.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CanonServiceDesk;

class QueueIntegration {
    const string Printer="Canon Service Desk CI";
    [StructLayout(LayoutKind.Sequential)] struct Defaults { public IntPtr Datatype,DevMode;public uint Access; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Doc { [MarshalAs(UnmanagedType.LPWStr)] public string Name;[MarshalAs(UnmanagedType.LPWStr)] public string Output;[MarshalAs(UnmanagedType.LPWStr)] public string Datatype; }
    [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool OpenPrinterW(string name,out IntPtr printer,ref Defaults defaults);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetPrinterW(IntPtr printer,uint level,IntPtr data,uint command);
    [DllImport("winspool.drv",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint StartDocPrinterW(IntPtr printer,uint level,ref Doc doc);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr printer,byte[] data,uint size,out uint written);
    [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr printer);
    static void Check(bool value,string name) { if(!value) throw new Exception(name+"; Win32="+Marshal.GetLastWin32Error());Console.WriteLine("PASS native: "+name); }
    static int Main(string[] args) {
        try {
            var log=new Journal(args[0]);IntPtr handle;var defaults=new Defaults { Access=12 };
            Check(OpenPrinterW(Printer,out handle,ref defaults),"open isolated queue");
            try {
                Check(SetPrinterW(handle,0,IntPtr.Zero,1),"pause isolated queue before adding data");
                var doc=new Doc { Name="Проверка UTF-8 — CI only",Datatype="RAW" };
                uint id=StartDocPrinterW(handle,1,ref doc);Check(id!=0,"create temporary spool job");
                byte[] bytes=Encoding.ASCII.GetBytes("CI TEST - NEVER SENT TO HARDWARE\r\n");uint written;
                Check(WritePrinter(handle,bytes,(uint)bytes.Length,out written) && written==bytes.Length,"spool bytes to paused queue");
                Check(EndDocPrinter(handle),"finish spooling");
                var request=new ServiceRequest { Printer=Printer,Operation="queue-list" };
                var list=PrintSpooler.Run(request,log);var job=list.Jobs.Single(j=>j.Id==id);
                Check(job.Document==doc.Name,"read real JOB_INFO_1 Unicode fields");
                request.Job=job;request.Operation="queue-pause";list=PrintSpooler.Run(request,log);
                Check((list.Jobs.Single(j=>j.Id==id).Status & 1)!=0,"pause selected real job");
                request.Operation="queue-resume";list=PrintSpooler.Run(request,log);
                Check((list.Jobs.Single(j=>j.Id==id).Status & 1)==0,"resume selected real job while queue remains paused");
                request.Operation="queue-delete";PrintSpooler.Run(request,log);request.Operation="queue-list";
                for(int i=0;i<50;i++) { list=PrintSpooler.Run(request,log);if(!list.Jobs.Any(j=>j.Id==id)) break;Thread.Sleep(100); }
                Check(!list.Jobs.Any(j=>j.Id==id),"delete selected real job");
                Console.WriteLine("Windows spooler integration passed; no physical printer used.");
            } finally { ClosePrinter(handle); }
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
}
