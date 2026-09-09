using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CanonServiceDesk {
    public static class NativeUsb {
        [StructLayout(LayoutKind.Sequential)] public struct DevInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] public struct IfInfo { public uint Size; public Guid ClassGuid; public uint Flags; public UIntPtr Reserved; }
        static readonly Guid UsbPrintGuid = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        static readonly IntPtr Invalid = new IntPtr(-1);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid,string enumerator,IntPtr hwnd,uint flags);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid,string enumerator,IntPtr hwnd,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref DevInfo info);
        [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr device,ref Guid guid,uint index,ref IfInfo info);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,ref IfInfo info,IntPtr detail,uint size,out uint needed,ref DevInfo device);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,ref DevInfo info,uint property,out uint type,byte[] data,uint size,out uint needed);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set,ref DevInfo info,StringBuilder id,uint size,out uint needed);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint instance,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle,uint control,IntPtr input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);
        static DevInfo NewDev() { return new DevInfo { Size=(uint)Marshal.SizeOf(typeof(DevInfo)) }; }
        static string Property(IntPtr set,ref DevInfo d,uint n) {
            uint type,needed; byte[] b=new byte[8192];
            if(!SetupDiGetDeviceRegistryPropertyW(set,ref d,n,out type,b,(uint)b.Length,out needed)) return "";
            return Encoding.Unicode.GetString(b,0,(int)Math.Min(needed,(uint)b.Length)).TrimEnd('\0').Replace('\0',';');
        }
        static Device Describe(IntPtr set,ref DevInfo d) {
            var x=new Device { Name=Property(set,ref d,12),HardwareIds=Property(set,ref d,1),Manufacturer=Property(set,ref d,11),Service=Property(set,ref d,4),Class=Property(set,ref d,7) };
            if(x.Name.Length==0) x.Name=Property(set,ref d,0);
            var sb=new StringBuilder(2048); uint n,status,problem;
            if(SetupDiGetDeviceInstanceIdW(set,ref d,sb,(uint)sb.Capacity,out n)) x.InstanceId=sb.ToString();
            x.ProblemCodeKnown=CM_Get_DevNode_Status(out status,out problem,d.DevInst,0)==0;
            x.ProblemCode=problem;
            return x;
        }
        static bool IsCanon(Device d) { return d.HardwareIds.IndexOf("VID_04A9",StringComparison.OrdinalIgnoreCase)>=0 || d.Manufacturer.IndexOf("Canon",StringComparison.OrdinalIgnoreCase)>=0 || d.Name.IndexOf("Canon",StringComparison.OrdinalIgnoreCase)>=0; }
        public static Snapshot Scan(Journal log) {
            var s=new Snapshot(); log.Save(s);
            log.Add("INFO","scan.start","Поиск подключённых устройств Canon; запросы записи отсутствуют.");
            try { EnumerateDevices(s,log); } catch(Exception e) { s.Errors.Add("PnP: "+e.Message); log.Add("ERROR","pnp",e.ToString()); }
            log.Save(s);
            try { EnumerateUsb(s,log); } catch(Exception e) { s.Errors.Add("USB: "+e.Message); log.Add("ERROR","usb",e.ToString()); }
            try {
                foreach(string p in PrinterSettings.InstalledPrinters) if(p.IndexOf("Canon",StringComparison.OrdinalIgnoreCase)>=0 || p.IndexOf("G2010",StringComparison.OrdinalIgnoreCase)>=0) s.Printers.Add(p);
                log.Add("INFO","queues","Очереди Canon",s.Printers);
            } catch(Exception e) { s.Errors.Add("Очереди: "+e.Message); log.Add("ERROR","queues",e.ToString()); }
            s.Completed=true; log.Save(s); log.Add("INFO","scan.end","Проверка завершена."); return s;
        }
        static void EnumerateDevices(Snapshot s,Journal log) {
            IntPtr set=SetupDiGetClassDevsW(IntPtr.Zero,null,IntPtr.Zero,6); // PRESENT | ALLCLASSES
            if(set==Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                for(uint i=0;;i++) {
                    DevInfo d=NewDev();
                    if(!SetupDiEnumDeviceInfo(set,i,ref d)) { int e=Marshal.GetLastWin32Error(); if(e!=259) throw new Win32Exception(e); break; }
                    Device item=Describe(set,ref d);
                    if(IsCanon(item)) { s.Devices.Add(item); log.Add("INFO","pnp.device",item.Name,item); }
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
        }
        static void EnumerateUsb(Snapshot s,Journal log) {
            Guid guid=UsbPrintGuid; IntPtr set=SetupDiGetClassDevsW(ref guid,null,IntPtr.Zero,18); // PRESENT | DEVICEINTERFACE
            if(set==Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                for(uint i=0;;i++) {
                    var face=new IfInfo { Size=(uint)Marshal.SizeOf(typeof(IfInfo)) };
                    if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref face)) { int e=Marshal.GetLastWin32Error(); if(e!=259) throw new Win32Exception(e); break; }
                    DevInfo d=NewDev(); uint needed;
                    SetupDiGetDeviceInterfaceDetailW(set,ref face,IntPtr.Zero,0,out needed,ref d);
                    int sizeError=Marshal.GetLastWin32Error();
                    if(sizeError!=122 || needed<8 || needed>1048576) throw new Win32Exception(sizeError,"Не удалось получить размер USB-интерфейса.");
                    IntPtr detail=Marshal.AllocHGlobal((int)needed);
                    try {
                        Marshal.WriteInt32(detail,IntPtr.Size==8 ? 8:6);
                        if(!SetupDiGetDeviceInterfaceDetailW(set,ref face,detail,needed,out needed,ref d)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                        Device device=Describe(set,ref d);
                        if(!IsCanon(device)) continue;
                        var u=new UsbResult { Name=device.Name,InstanceId=device.InstanceId,Path=path,ErrorStage="Ожидание открытия" };
                        s.Usb.Add(u); log.Save(s);
                        Probe(u,log); log.Save(s);
                    } finally { Marshal.FreeHGlobal(detail); }
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
        }
        static void Probe(UsbResult u,Journal log) {
            log.Add("INFO","usb.open.begin","Открытие интерфейса для чтения идентификатора",new { u.Path, DesiredAccess=0, ShareMode=3 });
            using(SafeFileHandle h=CreateFileW(u.Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(h.IsInvalid) { u.Win32Error=Marshal.GetLastWin32Error(); u.ErrorStage="CreateFileW"; log.Add("ERROR","usb.open",Rules.Win32(u.Win32Error),new { u.Win32Error }); return; }
                log.Add("INFO","usb.open","Интерфейс открыт.");
                foreach(int capacity in new[]{4094,1024}) {
                    var b=new byte[capacity]; uint returned;
                    log.Add("INFO","usb.id.request","IEEE 1284 GET_DEVICE_ID",new { Ioctl="0x00220034",OutputCapacity=capacity,InputLength=0 });
                    var sw=Stopwatch.StartNew();
                    bool ok=DeviceIoControl(h,0x220034,IntPtr.Zero,0,b,(uint)b.Length,out returned,IntPtr.Zero);
                    int error=ok ? 0:Marshal.GetLastWin32Error(); sw.Stop();
                    byte[] actual=ok ? b.Take((int)Math.Min(returned,(uint)b.Length)).ToArray():new byte[0];
                    log.Add(ok ? "INFO":"ERROR","usb.id.response",ok ? "Ответ получен.":Rules.Win32(error),new { Success=ok,Win32Error=error,Returned=returned,ElapsedMs=sw.ElapsedMilliseconds,Hex=DeviceIdParser.Hex(actual) });
                    u.Win32Error=error; u.ErrorStage="DeviceIoControl(GET_1284_ID)";
                    if(ok) {
                        u.RawHex=DeviceIdParser.Hex(actual); u.DeviceId=DeviceIdParser.Decode(actual);
                        u.Model=DeviceIdParser.Field(u.DeviceId,"MDL"); if(u.Model.Length==0) u.Model=DeviceIdParser.Field(u.DeviceId,"MODEL");
                        u.ReadOk=u.DeviceId.Length>0;
                        if(!u.ReadOk) { u.ErrorStage="Пустой ответ IEEE 1284"; log.Add("WARN","usb.id.empty","Успех Win32 без данных не подтверждает исправность связи."); }
                        break;
                    }
                    if(error!=23 || capacity==1024) break;
                    log.Add("INFO","usb.id.retry","После CRC повторяем только чтение с меньшим буфером.");
                }
            }
        }
    }
}
