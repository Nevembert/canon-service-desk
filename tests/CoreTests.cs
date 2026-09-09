using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using CanonServiceDesk;

class CoreTests {
    static int count;
    static void Check(bool condition,string name) { count++;if(!condition) throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name); }
    static int Main() {
        try {
            byte[] text=Encoding.ASCII.GetBytes("MFG:Canon;MDL:G2010 series;");
            byte[] prefixed=new byte[text.Length+3];prefixed[1]=(byte)(text.Length+2);Array.Copy(text,0,prefixed,2,text.Length);
            Check(DeviceIdParser.Decode(prefixed)==Encoding.ASCII.GetString(text),"IEEE 1284 prefix and trailing NUL");
            Check(DeviceIdParser.Decode(text)==Encoding.ASCII.GetString(text),"unprefixed identifier");
            Check(DeviceIdParser.Decode(new byte[0])=="","empty USB reply");
            Check(DeviceIdParser.Decode(new byte[]{0,2})=="","prefix-only reply");
            Check(DeviceIdParser.Field("MFG:Canon;MDL:G2010 series;","MDL")=="G2010 series","model extraction");
            Check(DeviceIdParser.Field("MODEL:G2010 series;","MDL")=="","no substring model match");
            Check(DeviceIdParser.Decode(new byte[]{0,40,77,70,71,58,67})=="MFG:C","truncated input stays bounded");
            Check(Rules.ServiceTool("006").Contains("не доказывает"),"006 does not diagnose bricked service mode");
            Check(Rules.Win32(6).Contains("дескриптор"),"Win32 6 has its own meaning");
            Check(!Rules.Win32(5).Contains("не распознаёт"),"Win32 access denied is not Service Tool 005");
            var s=new Snapshot { Completed=true };
            s.Devices.Add(new Device {Name="G2010",ProblemCodeKnown=true,ProblemCode=28});
            s.Usb.Add(new UsbResult {Name="Canon Device",ReadOk=true,Model="G2010 series",RawHex="00 02"});
            var findings=Rules.Diagnose(s);
            Check(findings.Any(f=>f.Title.Contains("USB отвечает")) && findings.Any(f=>f.Title.Contains("Проблема драйвера")),"normal interface missing driver can coexist with service USB");
            var incomplete=new Snapshot();
            Check(!Rules.Diagnose(incomplete).Any(f=>f.Title.Contains("Canon не найден")),"partial scan cannot prove device absence");
            var empty=new Snapshot { Completed=true };empty.Usb.Add(new UsbResult());
            Check(Rules.Diagnose(empty).Any(f=>f.Action.Contains("идентификатор не получен")),"Win32 success with empty response is inconclusive");
            var round=Json.Decode<Snapshot>(Json.Encode(s));
            Check(round.Devices[0].ProblemCode==28 && round.Usb[0].ReadOk,"report JSON round trip");
            Check(WindowsArgs.Quote("Canon G2010")=="\"Canon G2010\"","Windows argument with spaces");
            Check(WindowsArgs.Quote("a\"b")=="\"a\\\"b\"","Windows embedded quote");
            Check(WindowsArgs.Quote("C:\\path\\")=="\"C:\\path\\\\\"","Windows trailing slash");
            Check(Marshal.SizeOf(typeof(NativeUsb.DevInfo))==(IntPtr.Size==8 ? 32:28),"native SP_DEVINFO_DATA ABI");
            Check(Marshal.SizeOf(typeof(NativeUsb.IfInfo))==(IntPtr.Size==8 ? 32:28),"native SP_DEVICE_INTERFACE_DATA ABI");
            string temp=Path.Combine(Path.GetTempPath(),"canon-tests-"+Guid.NewGuid().ToString("N"));
            try {
                var log=new Journal(temp);log.Add("ERROR","usb.test","Проверка русских символов",new { Win32Error=23 });log.Save(s);
                Check(File.ReadAllText(Path.Combine(temp,"journal.txt")).Contains("русских"),"UTF-8 readable journal");
                var line=File.ReadAllLines(Path.Combine(temp,"events.jsonl"))[0];
                Check(Json.Decode<System.Collections.Generic.Dictionary<string,object>>(line).ContainsKey("data"),"structured event saved");
                Check(Json.Decode<Snapshot>(File.ReadAllText(Path.Combine(temp,"report.json"))).Completed,"report checkpoint");
            } finally { Directory.Delete(temp,true); }
            Console.WriteLine(count+" core tests passed.");ServiceTests.Run();return 0;
        } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
}
