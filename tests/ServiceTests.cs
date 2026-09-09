using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CanonServiceDesk;

static class ServiceTests {
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name); }
    static void Reject(Action action,string name) { bool failed=false;try { action(); } catch(FormatException) { failed=true; } catch(InvalidOperationException) { failed=true; }Check(failed,name); }
    public static int Run() {
        var a=CounterParser.Parse("Total 7522 Pages\r\nPrint 7469 Pages\r\nBlank 53 Pages");
        Check(a.Total==7522 && a.Printed==7469 && a.Blank==53,"counter report from printed service sheet");
        Check(CounterParser.Parse("total=0\nPrint:0\nBlank 0").Total==0,"real zero is distinct from missing counter");
        Check(CounterParser.Parse("D=070.2 TPAGE=7522 ST=0").Total==7522,"TPAGE token in EEPROM text");
        Check(!CounterParser.Parse("Print 10").Total.HasValue,"absent total is not inferred");
        Reject(()=>CounterParser.Parse("Total 10\nTotal 20"),"conflicting reports are rejected");
        Reject(()=>CounterParser.Parse("Total 10\nTPAGE=20"),"conflicting aliases are rejected");
        Reject(()=>CounterParser.Parse("Total 10\nPrint 8\nBlank 3"),"inconsistent page arithmetic is rejected");
        Reject(()=>CounterParser.Parse("Total 9223372036854775808"),"counter overflow is rejected");
        Reject(()=>CounterParser.Parse("Total -10"),"negative counter is rejected");
        Reject(()=>CounterParser.Parse("Total 7,522"),"ambiguous number format is rejected");
        Reject(()=>CounterParser.Parse("D=70.1"),"waste number is never treated as page count");
        Reject(()=>CounterParser.Parse(new string('x',65537)),"oversized report is rejected");
        Check(CounterParser.Delta(a,CounterParser.Parse("Total 7529")).Contains("7."),"counter difference from captured readings");
        Check(CounterParser.Delta(a,CounterParser.Parse("Total 0")).Contains("не подтверждён"),"decrease does not prove hardware reset");
        Check(BjlCommands.Advertised("MFG:Canon;MDL:G2010 series;CMD:BSCCe,BJL,IVEC;"),"BJL advertised as exact capability");
        Check(!BjlCommands.Advertised("MFG:Canon;CMD:NOTBJL;"),"capability substring is not accepted");
        Check(!BjlCommands.Advertised("MFG:Epson;CMD:BJL;"),"BJL is restricted to Canon identity");
        Check(!BjlCommands.Advertised("MFG:Canon;MDL:G2010 series;"),"unknown capability never enables a write");
        Check(BjlCommands.Advertised("MANUFACTURER:Canon;COMMAND SET:BJL;"),"long IEEE 1284 field aliases");
        var expected=new byte[]{0x1b,0x5b,0x4b,2,0,0,0x1f}.Concat(Encoding.ASCII.GetBytes("BJLSTART\n@TestPrint=NozzleCheck\nBJLEND\n")).ToArray();
        Check(BjlCommands.Build("nozzle").SequenceEqual(expected),"nozzle command matches BJL wire reference");
        Reject(()=>BjlCommands.Build("absorber-reset"),"no invented absorber command");
        Reject(()=>BjlCommands.Build("page-reset"),"no invented page count command");
        Reject(()=>BjlCommands.Build("clean-all\n@FactoryReset"),"arbitrary BJL injection is rejected");
        var job=new QueueJob { Id=1,Document="Тест",Owner="User",Submitted="2026-09-09T00:00:00.000Z" };
        Check(PrintSpooler.SameJob(job,Json.Decode<QueueJob>(Json.Encode(job))),"selected job fingerprint survives request serialization");
        Check(!PrintSpooler.SameJob(job,new QueueJob { Id=1,Document="Другой" }),"reused job ID is rejected");
        Check(!PrintSpooler.SameJob(job,null),"disappeared job is rejected");
        Check(Marshal.SizeOf(typeof(PrintSpooler.JobInfo1))==(IntPtr.Size==8 ? 96:64),"JOB_INFO_1 layout matches Windows ABI");
        Check(PrintSpooler.StatusText(0x41).Contains("Пауза") && PrintSpooler.StatusText(0x41).Contains("Нет бумаги"),"combined queue status flags");
        string root=Path.Combine(Path.GetTempPath(),"canon-service-tests-"+Guid.NewGuid().ToString("N"));
        try {
            var history=new ServiceHistory(root);
            history.Append(new ServiceEntry { Device="Unit A",Kind="reading",Counters=a });
            history.Append(new ServiceEntry { Device="Unit A",Kind="baseline",Counters=a,Source="app_local" });
            history.Append(new ServiceEntry { Device="Unit B",Kind="reading",Counters=CounterParser.Parse("Total 100") });
            var rows=new ServiceHistory(root).Read("Unit A");
            Check(rows.Count==2 && rows[0].Counters.Total==7522 && rows[1].Source=="app_local","local baseline preserves original hardware reading across restart");
            Check(history.Read("Unit B").Count==1,"device histories stay isolated");
            history.Append(new ServiceEntry { Device="../../outside",Kind="procedure",Outcome="Прервано" });
            Check(Directory.GetFiles(root).Length==3,"device name cannot traverse history path");
            var saved=Json.Decode<CounterReading>(Json.Encode(a));
            Check(saved.Raw.Contains("7469") && saved.Source=="operator_text","counter provenance and original text preserved");
        } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
        Console.WriteLine(count+" service tests passed.");return count;
    }
}
