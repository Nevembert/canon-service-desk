// Exercise the real Frida observer with fake Windows calls, without USB writes.
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const hooks={},rows=[];
class Ptr {
  constructor(n,opts={}) {this.n=n;Object.assign(this,opts);}
  toString(){return String(this.n);} isNull(){return this.n===0;}
  equals(p){return this.n===p.n;} toUInt32(){return this.n>>>0;} toInt32(){return this.n|0;}
  readU32(){return this.value||0;} readUtf16String(){return this.text;} readCString(){return this.text;}
  readByteArray(n){assert(!this.forbid,'read pending buffer before completion');return (this.buffer||new Uint8Array(n)).slice(0,n).buffer;}
}
const ptr=n=>new Ptr(n);
const context={Map,Set,Array,Uint8Array,Date,Math,ptr,send:r=>rows.push(r),
 Process:{arch:'x64',findModuleByName:()=>({findExportByName:n=>({toString:()=>n,name:n})})},
 Interceptor:{attach:(p,cb)=>hooks[p.name]=cb}};
vm.runInNewContext(fs.readFileSync(require('path').join(__dirname,'../trace/usb_observer.js'),'utf8'),context);
function call(name,args,ret,error=0){const ctx={lastError:error};hooks[name].onEnter.call(ctx,args);hooks[name].onLeave.call(ctx,ptr(ret));}
call('CreateFileW',[new Ptr(1,{text:'C:\\unrelated.txt'}),ptr(0),ptr(3)],11);
assert(!rows.some(r=>r.event==='open'),'unrelated path captured');
call('CreateFileW',[new Ptr(1,{text:'\\\\?\\usb#vid_04a9&pid_1234#canon'}),ptr(0),ptr(3)],22);
const buffer=new Ptr(2,{buffer:Uint8Array.from([65,66]),forbid:true});
call('DeviceIoControl',[ptr(22),ptr(0x220034),ptr(0),ptr(0),buffer,ptr(2),new Ptr(3,{value:2}),ptr(99)],0,997);
assert(rows.some(r=>r.event==='pending'));
assert(!rows.some(r=>r.event==='response'));
buffer.forbid=false;
call('GetOverlappedResult',[ptr(22),ptr(99),new Ptr(3,{value:2}),ptr(1)],1);
assert(rows.some(r=>r.event==='response'&&r.output_hex==='41 42'&&r.success));
call('DeviceIoControl',[ptr(22),ptr(0x220034),ptr(0),ptr(0),buffer,ptr(5000),new Ptr(3),ptr(0)],0,23);
assert(rows.some(r=>r.event==='response'&&r.win32===23&&!r.success&&r.output_hex===''));
call('CloseHandle',[ptr(22)],1);
const before=rows.length;
call('DeviceIoControl',[ptr(22)],0,6);
assert.strictEqual(rows.length,before,'closed handle still tracked');
console.log('Observer tests passed: Canon filtering, pending completion, raw bytes, failure codes, closed handle cleanup.');
